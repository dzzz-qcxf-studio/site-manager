using SiteManager.Core.Validation;

namespace SiteManager.Core.Publishing;

public sealed class WebsiteValidationException(FolderValidationResult result)
    : Exception("The website folder did not pass validation.")
{
    public FolderValidationResult Result { get; } = result;
}
