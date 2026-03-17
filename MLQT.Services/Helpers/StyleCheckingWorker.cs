using ModelicaParser.DataTypes;
using ModelicaParser.SpellChecking;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
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

            // Build set of known model IDs for reference validation
            IReadOnlySet<string>? knownModelIds = null;
            if (_settings.ValidateModelReferences)
            {
                knownModelIds = _currentGraph.ModelNodes
                    .Select(n => n.Id)
                    .ToHashSet(StringComparer.Ordinal);
            }

            // Build set of known model names for spell checking context
            IReadOnlySet<string>? knownModelNames = null;
            if ((_settings.SpellCheckDescription || _settings.SpellCheckDocumentation) && _spellChecker != null)
            {
                knownModelNames = _currentGraph.ModelNodes
                    .Select(n => n.Id.Contains('.') ? n.Id[(n.Id.LastIndexOf('.') + 1)..] : n.Id)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            // Create callback for inherited icon checking (uses graph to resolve base classes)
            var baseClassHasIcon = _settings.ClassHasIcon
                ? StyleChecking.CreateBaseClassHasIconCallback(_currentGraph)
                : null;

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
                    if (node != null && !node.Definition.StyleRulesChecked)
                    {
                        var violations = StyleChecking.RunStyleChecking(node.Definition, _settings, modelId, knownModelIds, _spellChecker, knownModelNames,
                            isExcludedFromFormatting: _settings.IsModelExcludedFromFormatting(modelId),
                            baseClassHasIcon: baseClassHasIcon);

                        if (violations.Count > 0)
                            OnViolationFound?.Invoke(this, violations);

                        // Release parse tree after checking to free memory
                        node.Definition.ParsedCode = null;
                    }
                }
                catch
                {
                    // Skip models that fail to parse or check — don't stall the worker
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
