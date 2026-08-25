using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using ModelicaParser.SpellChecking;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using MLQT.Services.Checking;
using System.Collections.Concurrent;

namespace MLQT.Services.Helpers;

/// <summary>
/// Worker that performs that style checking on a specific library as part
/// of the StyleCheckingService
/// </summary>
public class StyleCheckingWorker
{
    private readonly ConcurrentQueue<string> _checkQueue = new();
    private readonly DirectedGraph _currentGraph;
    private readonly StyleCheckingSettings _settings;
    private readonly SpellChecker? _spellChecker;
    private bool _isRunning = false;
    private bool _stopRequested = false;
    private string _repositoryName;
    private int _processedCount = 0;

    public event EventHandler<List<LogMessage>>? OnViolationFound;
    public event Action? OnProgressChanged;
    public event EventHandler<string>? OnWorkCompleted;

    public StyleCheckingWorker(DirectedGraph graph, StyleCheckingSettings settings, string repositoryName, SpellChecker? spellChecker = null)
    {
        _currentGraph = graph;
        _repositoryName = repositoryName;
        _settings = settings;
        _spellChecker = spellChecker;
    }

    public void AddToQueue(string modelID)
    {
        _checkQueue.Enqueue(modelID);
    }

    public int QueuedCount
    {
        get
        {
            return _checkQueue.Count;
        }
    }

    public void StartProcessing()
    {
        _ = Task.Run(ProcessCheckQueueAsync);
    }

    public void CancelProcessing()
    {
        _stopRequested = true;
    }

    private Task ProcessCheckQueueAsync()
    {
        if (_isRunning)
            return Task.CompletedTask;

        _isRunning = true;
        try
        {
            // Drain the queue into a list for parallel processing
            var modelIds = new List<string>();
            while (_checkQueue.TryDequeue(out var modelId))
            {
                if (modelId != null)
                    modelIds.Add(modelId);
            }

            // Build the per-check context (known ids/names, icon callback) through the shared
            // StyleCheckContext so the GUI derives it identically to the CLI and MCP — reusing the
            // service's cached spell checker rather than rebuilding one.
            var context = StyleCheckContext.Build(_settings, _currentGraph, _spellChecker);

            // Process models in parallel with bounded concurrency
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1)
            };

            Parallel.ForEach(modelIds, parallelOptions, modelId =>
            {
                if (_stopRequested)
                    return;

                try
                {
                    var node = _currentGraph.GetNode<ModelNode>(modelId);
                    // Check every class, including non-standalone ones (replaceable/redeclare/inner/outer).
                    // CanBeStoredStandalone is a file-storage flag resolved non-deterministically by parse
                    // order, so filtering on it here made the GUI's finding set unstable and inconsistent
                    // with the CLI/MCP (which check all classes). StyleRulesChecked still dedups re-checks.
                    if (node != null && !node.Definition.StyleRulesChecked)
                    {
                        // Same per-model entry point (StyleCheckRunner → RunStyleChecking) as the CLI/MCP;
                        // it releases the parse tree after checking to bound memory.
                        var violations = StyleCheckRunner.Run(node, _settings, context);

                        if (violations.Count > 0)
                            OnViolationFound?.Invoke(this, violations);
                    }
                }
                catch (Exception ex)
                {
                    // Report it rather than dropping the class: one class that cannot be checked
                    // should not stop the worker, but silence here cost the class every finding it
                    // had and made the app's totals disagree with the CLI's for no visible reason.
                    OnViolationFound?.Invoke(this, [
                        new LogMessage(modelId, "Style warning", 0,
                            $"Checking this class failed ({ex.GetType().Name}: {ex.Message}). " +
                            "Its findings are missing from these results.")
                        {
                            Source = "StyleChecking",
                            RuleId = RuleIds.CheckFailed,
                        }
                    ]);
                }

                // Batch progress notifications — fire every 50 models instead of every model
                var count = Interlocked.Increment(ref _processedCount);
                if (count % 50 == 0)
                    OnProgressChanged?.Invoke();
            });

            // Final progress update
            OnProgressChanged?.Invoke();
        }
        finally
        {
            _isRunning = false;
            OnWorkCompleted?.Invoke(this, _repositoryName);
        }

        return Task.CompletedTask;
    }
}
