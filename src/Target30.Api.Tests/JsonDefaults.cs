using System.Text.Json;

namespace Target30.Api.Tests;

internal static class JsonDefaults
{
    // A API serializa em camelCase (padrão web do ASP.NET Core); HttpClient.ReadFromJsonAsync
    // usa opções "puras" do System.Text.Json por padrão, que são case-sensitive.
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
