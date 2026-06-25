using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TranslateText.Core;
using TranslateText.Models;

namespace TranslateText.Services
{
    /// <summary>
    /// Orchestrates translation independently from AutoCAD transactions and UI concerns.
    /// Identical source strings are translated once and fanned out to every matching entity.
    /// </summary>
    public sealed class TranslationBatchProcessor
    {
        public const int DefaultMaxConcurrency = 8;

        private readonly int _maxConcurrency;
        private readonly Func<string, string, string, SemaphoreSlim, Task<string>> _translateAsync;

        public TranslationBatchProcessor(int maxConcurrency = DefaultMaxConcurrency)
            : this(TranslationService.ProcessAsync, maxConcurrency)
        {
        }

        internal TranslationBatchProcessor(
            Func<string, string, string, SemaphoreSlim, Task<string>> translateAsync,
            int maxConcurrency)
        {
            _translateAsync = translateAsync ?? throw new ArgumentNullException(nameof(translateAsync));
            if (maxConcurrency < 1) throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
            _maxConcurrency = maxConcurrency;
        }

        public async Task<TranslationBatchResult> ProcessAsync(
            IList<TextEntityData> items,
            string sourceLanguage,
            string targetLanguage,
            TextCaseOption textCase)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            var groups = new Dictionary<string, List<TextEntityData>>(StringComparer.Ordinal);
            foreach (TextEntityData item in items)
            {
                if (item == null) continue;

                string sourceText = item.OriginalText ?? string.Empty;
                if (!groups.TryGetValue(sourceText, out List<TextEntityData> group))
                {
                    group = new List<TextEntityData>();
                    groups.Add(sourceText, group);
                }
                group.Add(item);
            }

            var tasks = new List<Task<bool>>(groups.Count);
            using (var semaphore = new SemaphoreSlim(_maxConcurrency))
            {
                foreach (KeyValuePair<string, List<TextEntityData>> group in groups)
                {
                    tasks.Add(ProcessGroupAsync(
                        group.Key,
                        group.Value,
                        sourceLanguage,
                        targetLanguage,
                        textCase,
                        semaphore));
                }

                bool[] outcomes = await Task.WhenAll(tasks).ConfigureAwait(false);
                int failedTextCount = 0;
                foreach (bool succeeded in outcomes)
                {
                    if (!succeeded) failedTextCount++;
                }

                return new TranslationBatchResult(items.Count, groups.Count, failedTextCount);
            }
        }

        private async Task<bool> ProcessGroupAsync(
            string sourceText,
            List<TextEntityData> items,
            string sourceLanguage,
            string targetLanguage,
            TextCaseOption textCase,
            SemaphoreSlim semaphore)
        {
            string processedText;
            bool succeeded = true;
            try
            {
                string translated = await _translateAsync(
                    sourceText,
                    sourceLanguage,
                    targetLanguage,
                    semaphore).ConfigureAwait(false);
                processedText = TextCaseHelper.ApplyCaseSafe(translated, textCase);
            }
            catch
            {
                processedText = sourceText;
                succeeded = false;
            }

            foreach (TextEntityData item in items)
            {
                item.ProcessedText = processedText;
            }
            return succeeded;
        }
    }

    public sealed class TranslationBatchResult
    {
        public TranslationBatchResult(int itemCount, int uniqueTextCount, int failedTextCount)
        {
            ItemCount = itemCount;
            UniqueTextCount = uniqueTextCount;
            FailedTextCount = failedTextCount;
        }

        public int ItemCount { get; }
        public int UniqueTextCount { get; }
        public int FailedTextCount { get; }
    }
}
