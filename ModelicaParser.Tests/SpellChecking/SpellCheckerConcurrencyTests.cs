using ModelicaParser.SpellChecking;
using Xunit;

namespace ModelicaParser.Tests.SpellChecking;

/// <summary>
/// The spell checker is read from every core at once, and written to occasionally (backlog B128).
/// </summary>
/// <remarks>
/// <para><see cref="SpellChecker.IsCorrect"/> is called once per word of every description and
/// documentation string in a library — millions of times per check, in parallel. It used to take a
/// lock around the accepted-word list on every one of those calls: a micro-benchmark scaled 4.8x on
/// 24 threads rather than the ~24x the work allows, and each call cost 3.2µs. The list is now an
/// immutable snapshot swapped on write, and the dictionary answer is remembered per word; the same
/// benchmark reads 0.11µs.</para>
///
/// <para>That trades a lock for two things that need holding down: a word accepted mid-run must take
/// effect immediately (<c>AddCustomWord_BecomesCorrect</c> in the sibling class covers that, and it
/// is precisely the assertion a cache breaks), and reading while another thread accepts must not tear
/// or throw.</para>
/// </remarks>
public class SpellCheckerConcurrencyTests
{
    [Fact]
    public void ReadingFromEveryCoreGivesOneAnswerPerWord()
    {
        var checker = SpellChecker.Create(customWords: ["Claytex"]);

        var answers = new System.Collections.Concurrent.ConcurrentBag<(string Word, bool Correct)>();
        Parallel.For(0, 200, _ =>
        {
            foreach (var word in new[] { "temperature", "Claytex", "xyzzyplugh", "pressure" })
                answers.Add((word, checker.IsCorrect(word)));
        });

        foreach (var group in answers.GroupBy(a => a.Word))
            Assert.True(group.Select(a => a.Correct).Distinct().Count() == 1,
                $"'{group.Key}' was answered both ways across threads");

        Assert.All(answers.Where(a => a.Word == "xyzzyplugh"), a => Assert.False(a.Correct));
        Assert.All(answers.Where(a => a.Word == "Claytex"), a => Assert.True(a.Correct));
    }

    [Fact]
    public void AcceptingAWordWhileOtherThreadsReadIsSafe_AndTakesEffect()
    {
        // The write path copies the set and swaps it, so a reader sees the old snapshot or the new
        // one and never one being mutated. Before the change this was a lock; the risk being covered
        // is that removing it left a reader able to see a half-built set.
        var checker = SpellChecker.Create();
        var word = "zzqqxlibrary";

        // Not zero: Create seeds the accepted list with the bundled Modelica and engineering terms.
        var before = checker.CustomWords.Count;

        Assert.False(checker.IsCorrect(word));   // and caches that answer

        using var readers = new CancellationTokenSource();
        var reading = Task.Run(() =>
        {
            while (!readers.IsCancellationRequested)
            {
                checker.IsCorrect("temperature");
                checker.IsCorrect(word);
                _ = checker.CustomWords.Count;
            }
        });

        for (var i = 0; i < 50; i++)
            checker.AddCustomWord($"filler{i}");

        checker.AddCustomWord(word);

        readers.Cancel();
        reading.GetAwaiter().GetResult();        // rethrows anything the reader threw

        Assert.True(checker.IsCorrect(word), "the accepted word did not take effect");
        Assert.Contains(word, checker.CustomWords);
        Assert.Equal(before + 51, checker.CustomWords.Count);   // 50 fillers and the word itself
    }

    [Fact]
    public void TheRememberedAnswerIsPerWordAndNotPerClass()
    {
        // Context words are the caller's, differ per class, and are deliberately not part of what is
        // remembered. A word that is only acceptable because *this* class declares it must not become
        // acceptable everywhere.
        var checker = SpellChecker.Create();
        IReadOnlySet<string> context = new HashSet<string>(StringComparer.Ordinal) { "myOddComponent" };

        Assert.True(checker.IsCorrect("myOddComponent", context));
        Assert.False(checker.IsCorrect("myOddComponent"));
        Assert.True(checker.IsCorrect("myOddComponent", context));
    }
}
