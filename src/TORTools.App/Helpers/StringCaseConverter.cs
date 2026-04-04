using System.Text;

namespace TORTools.App.Helpers;

/// <summary>
/// Utility for converting between different string casing conventions.
/// </summary>
public static class StringCaseConverter
{
    /// <summary>
    /// Converts PascalCase to snake_case.
    /// E.g., "HeadArmor" → "head_armor"
    /// </summary>
    public static string ConvertPascalToSnakeCase(string value)
    {
        var result = new StringBuilder();
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                    result.Append('_');
                result.Append(char.ToLower(c));
            }
            else
            {
                result.Append(c);
            }
        }
        return result.ToString();
    }

    /// <summary>
    /// Converts snake_case to PascalCase.
    /// E.g., "head_armor" → "HeadArmor"
    /// </summary>
    public static string ConvertSnakeToPascalCase(string value)
    {
        var result = new StringBuilder();
        bool capitalizeNext = true;
        foreach (char c in value)
        {
            if (c == '_')
            {
                capitalizeNext = true;
            }
            else
            {
                result.Append(capitalizeNext ? char.ToUpper(c) : c);
                capitalizeNext = false;
            }
        }
        return result.ToString();
    }
}
