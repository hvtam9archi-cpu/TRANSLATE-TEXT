using System;
using System.Collections.Generic;
using System.Diagnostics;
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

            var tasks = new List<Task<TranslationGroupOutcome>>(groups.Count);
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

                TranslationGroupOutcome[] outcomes = await Task.WhenAll(tasks)
                    .ConfigureAwait(false);
                int failedTextCount = 0;
                var failureMessages = new List<string>();
                foreach (TranslationGroupOutcome outcome in outcomes)
                {
                    if (outcome.Succeeded) continue;

                    failedTextCount++;
                    if (failureMessages.Count < 5)
                        failureMessages.Add(outcome.ErrorMessage);
                }

                return new TranslationBatchResult(
                    items.Count,
                    groups.Count,
                    failedTextCount,
                    failureMessages);
            }
        }

        private async Task<TranslationGroupOutcome> ProcessGroupAsync(
            string sourceText,
            List<TextEntityData> items,
            string sourceLanguage,
            string targetLanguage,
            TextCaseOption textCase,
            SemaphoreSlim semaphore)
        {
            string processedText;
            try
            {
                string translated = await _translateAsync(
                    sourceText,
                    sourceLanguage,
                    targetLanguage,
                    semaphore).ConfigureAwait(false);
                processedText = TextCaseHelper.ApplyCaseSafe(translated, textCase);
            }
            catch (Exception exception)
            {
                processedText = sourceText;
                string preview = GetSafePreview(sourceText);
                Trace.TraceError(
                    $"[TranslateText] Translation failed for '{preview}': {exception}");

                foreach (TextEntityData item in items)
                    item.ProcessedText = processedText;

                return TranslationGroupOutcome.Failed(
                    $"Không thể dịch \"{preview}\": {exception.Message}");
            }

            foreach (TextEntityData item in items)
            {
                item.ProcessedText = processedText;
            }
            return TranslationGroupOutcome.Success;
        }

        private static string GetSafePreview(string text)
        {
            string preview = (text ?? string.Empty)
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
            return preview.Length <= 80 ? preview : preview.Substring(0, 77) + "...";
        }

        private sealed class TranslationGroupOutcome
        {
            public static readonly TranslationGroupOutcome Success =
                new TranslationGroupOutcome(true, null);

            private TranslationGroupOutcome(bool succeeded, string errorMessage)
            {
                Succeeded = succeeded;
                ErrorMessage = errorMessage;
            }

            public bool Succeeded { get; }
            public string ErrorMessage { get; }

            public static TranslationGroupOutcome Failed(string errorMessage)
            {
                return new TranslationGroupOutcome(false, errorMessage);
            }
        }
    }

    public sealed class TranslationBatchResult
    {
        public TranslationBatchResult(int itemCount, int uniqueTextCount, int failedTextCount)
            : this(
                itemCount,
                uniqueTextCount,
                failedTextCount,
                Array.Empty<string>())
        {
        }

        public TranslationBatchResult(
            int itemCount,
            int uniqueTextCount,
            int failedTextCount,
            IReadOnlyList<string> failureMessages)
        {
            ItemCount = itemCount;
            UniqueTextCount = uniqueTextCount;
            FailedTextCount = failedTextCount;
            if (failureMessages == null)
                throw new ArgumentNullException(nameof(failureMessages));
            FailureMessages = new List<string>(failureMessages);
        }

        public int ItemCount { get; }
        public int UniqueTextCount { get; }
        public int FailedTextCount { get; }
        public IReadOnlyList<string> FailureMessages { get; }
    }
}
