namespace RevitMCPCommandSet.Services.Views
{
    /// <summary>
    /// Hands out view names that are not used in the model, adding " (2)", " (3)"… on a clash.
    /// Names are compared case-insensitively against every view in the document and every
    /// name reserved since the registry was built.
    /// </summary>
    public sealed class ViewNameRegistry
    {
        private readonly HashSet<string> _used;

        public ViewNameRegistry(Document doc)
        {
            _used = new HashSet<string>(
                new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(v => v.Name),
                StringComparer.OrdinalIgnoreCase);
        }

        public string Propose(string baseName)
        {
            if (!_used.Contains(baseName))
                return baseName;

            for (var n = 2; ; n++)
            {
                var candidate = $"{baseName} ({n})";
                if (!_used.Contains(candidate))
                    return candidate;
            }
        }

        public void Reserve(string name) => _used.Add(name);
    }
}
