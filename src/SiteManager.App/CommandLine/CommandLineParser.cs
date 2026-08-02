namespace SiteManager.App.CommandLine;

public sealed class CliUsageException(string message) : Exception(message);

public sealed class CliInvocation
{
    private readonly IReadOnlyDictionary<string, string?> _options;

    internal CliInvocation(
        string command,
        IReadOnlyDictionary<string, string?> options,
        bool json,
        bool confirmed,
        bool launch,
        bool help,
        bool version)
    {
        Command = command;
        _options = options;
        Json = json;
        Confirmed = confirmed;
        Launch = launch;
        Help = help;
        Version = version;
    }

    public string Command { get; }

    public bool Json { get; }

    public bool Confirmed { get; }

    public bool Launch { get; }

    public bool Help { get; }

    public bool Version { get; }

    public string? Get(string name) => _options.TryGetValue(name, out var value) ? value : null;

    public string GetRequired(string name)
    {
        var value = Get(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CliUsageException($"Missing required option: --{name}.");
        }

        return value;
    }
}

public static class CommandLineParser
{
    private static readonly HashSet<string> ValueOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "archive",
        "name",
        "note",
        "settings",
        "site",
        "source",
        "status"
    };

    private static readonly HashSet<string> BooleanOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "help",
        "json",
        "launch",
        "yes"
    };

    public static CliInvocation Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var command = string.Empty;
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var json = false;
        var confirmed = false;
        var launch = false;
        var help = false;
        var version = false;

        for (var index = 0; index < args.Count; index++)
        {
            var token = args[index];
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            if (!token.StartsWith("-", StringComparison.Ordinal))
            {
                if (!string.IsNullOrEmpty(command))
                {
                    throw new CliUsageException($"Unexpected positional argument: {token}.");
                }

                command = token.Trim().ToLowerInvariant();
                continue;
            }

            var option = token.TrimStart('-');
            if (option.Length == 0)
            {
                throw new CliUsageException("Invalid empty option.");
            }

            var separator = option.IndexOf('=');
            string? inlineValue = null;
            if (separator >= 0)
            {
                inlineValue = option[(separator + 1)..];
                option = option[..separator];
            }

            option = option.ToLowerInvariant() switch
            {
                "h" => "help",
                "v" => "version",
                _ => option.ToLowerInvariant()
            };

            if (option == "version")
            {
                version = true;
                continue;
            }

            if (BooleanOptions.Contains(option))
            {
                if (inlineValue is not null)
                {
                    throw new CliUsageException($"Option --{option} does not accept a value.");
                }

                switch (option)
                {
                    case "help":
                        help = true;
                        break;
                    case "json":
                        json = true;
                        break;
                    case "launch":
                        launch = true;
                        break;
                    case "yes":
                        confirmed = true;
                        break;
                }

                continue;
            }

            if (!ValueOptions.Contains(option))
            {
                throw new CliUsageException($"Unknown option: --{option}.");
            }

            var value = inlineValue;
            if (value is null)
            {
                if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    throw new CliUsageException($"Missing value for option --{option}.");
                }

                value = args[++index];
            }

            options[option] = value;
        }

        if (string.IsNullOrEmpty(command))
        {
            command = version ? "version" : "help";
            help = !version;
        }

        return new CliInvocation(command, options, json, confirmed, launch, help, version);
    }
}
