namespace RevitMCPCommandSet.Services.Views
{
    /// <summary>
    /// Failure preprocessor for the Interior Elevation tools: deletes warnings (recording their text),
    /// and rolls the transaction back on any error, so Revit never shows a dialog.
    /// </summary>
    public sealed class ElevationFailurePreprocessor : IFailuresPreprocessor
    {
        public List<string> Warnings { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            var hasError = false;
            foreach (var message in failuresAccessor.GetFailureMessages())
            {
                if (message.GetSeverity() == FailureSeverity.Warning)
                {
                    Warnings.Add(message.GetDescriptionText());
                    failuresAccessor.DeleteWarning(message);
                }
                else
                {
                    hasError = true;
                    Errors.Add(message.GetDescriptionText());
                }
            }

            return hasError ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }

        /// <summary>
        /// Runs <paramref name="body"/> in its own transaction guarded by this preprocessor.
        /// Returns null when the transaction committed; otherwise the transaction is rolled back
        /// (nothing it created remains) and the error text is returned.
        /// Warnings Revit raised are appended to <paramref name="warnings"/>.
        /// </summary>
        public static string RunTransaction(Document doc, string name, Action body, List<string> warnings)
        {
            var preprocessor = new ElevationFailurePreprocessor();
            using (var transaction = new Transaction(doc, name))
            {
                var options = transaction.GetFailureHandlingOptions();
                options.SetFailuresPreprocessor(preprocessor);
                options.SetClearAfterRollback(true);
                options.SetDelayedMiniWarnings(false);
                options.SetForcedModalHandling(false);
                transaction.SetFailureHandlingOptions(options);

                try
                {
                    transaction.Start();
                    body();
                    var status = transaction.Commit();
                    warnings.AddRange(preprocessor.Warnings);
                    if (status == TransactionStatus.Committed)
                        return null;

                    return preprocessor.Errors.Count > 0
                        ? string.Join("; ", preprocessor.Errors)
                        : $"The transaction did not commit (status: {status}).";
                }
                catch (Exception ex)
                {
                    if (transaction.HasStarted() && !transaction.HasEnded())
                        transaction.RollBack();
                    return ex.Message;
                }
            }
        }
    }
}
