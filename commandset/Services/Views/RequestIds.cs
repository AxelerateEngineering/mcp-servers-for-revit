using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services.Views
{
    /// <summary>
    /// Element ids arrive from JSON as numbers, so they must be positive safe integers (≤ 2^53 − 1).
    /// </summary>
    internal static class RequestIds
    {
        private const long MaxSafeInteger = 9007199254740991;

        public static bool IsValid(long id) => id > 0 && id <= MaxSafeInteger;

        /// <summary>Returns the element with this id, or null when the id is invalid, too large for this Revit version, or unknown.</summary>
        public static Element GetElement(Document doc, long id)
        {
            if (!IsValid(id))
                return null;
            try
            {
                return doc.GetElement(id.ToRevitElementId());
            }
            catch (OverflowException)
            {
                return null;
            }
        }
    }
}
