using System;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Application
{
    /// Resolves the operator-selected local catalog entry and freezes it once per attempt.
    public sealed class ContentResolver
    {
        public ContentSnapshot ResolveForAttempt(ContentCatalogEntry selectedEntry)
        {
            if (selectedEntry == null)
                throw new ArgumentNullException(nameof(selectedEntry), "An explicit local catalog entry must be selected.");
            return ContentSnapshot.Freeze(selectedEntry);
        }
    }
}
