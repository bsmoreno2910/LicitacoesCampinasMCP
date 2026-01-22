using System.Globalization;
using System.Text.Json;

namespace LicitacoesCampinasMCP.Servicos;

/// <summary>
/// Métodos utilitários para parsing de JSON.
/// </summary>
public static class JsonHelper
{
    /// <summary>
    /// Obtém uma string de um JsonElement, aplicando Trim para remover espaços.
    /// </summary>
    public static string? GetString(JsonElement el, string prop)
    {
        try 
        { 
            if (el.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Null)
            {
                var str = v.GetString();
                return string.IsNullOrWhiteSpace(str) ? null : str.Trim();
            }
            return null; 
        }
        catch { return null; }
    }

    /// <summary>
    /// Obtém um inteiro de um JsonElement.
    /// </summary>
    public static int GetInt(JsonElement el, string prop)
    {
        try 
        { 
            if (el.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Null)
            {
                if (v.ValueKind == JsonValueKind.Number) return v.GetInt32();
                if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var i)) return i;
            }
            return 0;
        }
        catch { return 0; }
    }

    /// <summary>
    /// Obtém um inteiro nullable de um JsonElement.
    /// </summary>
    public static int? GetNullableInt(JsonElement el, string prop)
    {
        try 
        { 
            if (el.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Null)
            {
                if (v.ValueKind == JsonValueKind.Number) return v.GetInt32();
                if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var i)) return i;
            }
            return null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Obtém um decimal de um JsonElement, tratando formato brasileiro (48.000,00).
    /// </summary>
    public static decimal GetDecimal(JsonElement el, string prop)
    {
        try 
        { 
            if (el.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Null)
            {
                if (v.ValueKind == JsonValueKind.Number) return v.GetDecimal();
                if (v.ValueKind == JsonValueKind.String)
                {
                    var s = v.GetString()?.Replace(".", "").Replace(",", ".");
                    return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0;
                }
            }
            return 0;
        }
        catch { return 0; }
    }

    /// <summary>
    /// Obtém um boolean de um JsonElement.
    /// </summary>
    public static bool GetBool(JsonElement el, string prop)
    {
        try 
        { 
            if (el.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Null)
            {
                if (v.ValueKind == JsonValueKind.True) return true;
                if (v.ValueKind == JsonValueKind.False) return false;
                if (v.ValueKind == JsonValueKind.String)
                {
                    var s = v.GetString()?.ToLower();
                    return s == "true" || s == "1" || s == "sim" || s == "yes";
                }
            }
            return false;
        }
        catch { return false; }
    }

    /// <summary>
    /// Obtém um boolean nullable de um JsonElement.
    /// </summary>
    public static bool? GetNullableBool(JsonElement el, string prop)
    {
        try 
        { 
            if (el.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Null)
            {
                if (v.ValueKind == JsonValueKind.True) return true;
                if (v.ValueKind == JsonValueKind.False) return false;
                if (v.ValueKind == JsonValueKind.String)
                {
                    var s = v.GetString()?.ToLower();
                    if (s == "true" || s == "1" || s == "sim" || s == "yes") return true;
                    if (s == "false" || s == "0" || s == "nao" || s == "no") return false;
                }
            }
            return null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Obtém um decimal nullable de um JsonElement.
    /// </summary>
    public static decimal? GetNullableDecimal(JsonElement el, string prop)
    {
        try 
        { 
            if (el.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Null)
            {
                if (v.ValueKind == JsonValueKind.Number) return v.GetDecimal();
                if (v.ValueKind == JsonValueKind.String)
                {
                    var s = v.GetString()?.Replace(".", "").Replace(",", ".");
                    return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
                }
            }
            return null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Obtém um long nullable de um JsonElement.
    /// </summary>
    public static long? GetNullableLong(JsonElement el, string prop)
    {
        try 
        { 
            if (el.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Null)
            {
                if (v.ValueKind == JsonValueKind.Number) return v.GetInt64();
                if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var l)) return l;
            }
            return null;
        }
        catch { return null; }
    }
}
