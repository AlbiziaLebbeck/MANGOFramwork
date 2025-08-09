using System.Linq;
using System.Text.RegularExpressions;

public static class PlayerNameValidator
{
    // Offensive words list (lowercase)
    private static readonly string[] bannedWords =
    {
        "admin", "mod", "fuck", "shit", "bitch" // Add more
    };

    public static bool IsValidName(string name, out string reason)
    {
        // 1. Empty/null check
        if (string.IsNullOrWhiteSpace(name))
        {
            reason = "Name cannot be empty.";
            return false;
        }

        // 2. Regex: multiple languages, numbers, underscores, spaces, no lead/trail space/underscore
        string pattern = @"^(?![_\s])[\p{L}\p{Nd}_\s]{2,32}(?<![_\s])$";
        if (!Regex.IsMatch(name, pattern))
        {
            if (name.Length < 2 || name.Length > 32)
                reason = "Name must be between 2 and 32 characters.";
            else if (name.StartsWith(" ") || name.StartsWith("_"))
                reason = "Name cannot start with a space or underscore.";
            else if (name.EndsWith(" ") || name.EndsWith("_"))
                reason = "Name cannot end with a space or underscore.";
            else
                reason = "Name contains invalid characters.";
            return false;
        }

        // 3. No offensive words
        string lowerName = name.ToLower();
        if (bannedWords.Any(bw => lowerName.Contains(bw)))
        {
            reason = "Name contains inappropriate language.";
            return false;
        }

        // 4. No emoji
        if (Regex.IsMatch(name, @"\p{Cs}"))
        {
            reason = "Name cannot contain emoji.";
            return false;
        }

        // Passed all checks
        reason = "";
        return true;
    }
}
