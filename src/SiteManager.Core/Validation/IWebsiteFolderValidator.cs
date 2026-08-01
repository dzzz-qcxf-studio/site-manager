namespace SiteManager.Core.Validation;

public interface IWebsiteFolderValidator
{
    FolderValidationResult Validate(string root);
}
