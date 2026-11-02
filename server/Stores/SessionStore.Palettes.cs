namespace DotWatcher.Server;

// Saved palettes and their app-version assignments; PaletteProvider owns the rules.
public partial class SessionStore
{
    public IReadOnlyList<(string Id, string Json)> GetPalettes()
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, json FROM palettes";
        using var reader = cmd.ExecuteReader();
        var rows = new List<(string, string)>();
        while (reader.Read())
            rows.Add((reader.GetString(0), reader.GetString(1)));
        return rows;
    }

    public string? GetPaletteJson(string id)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT json FROM palettes WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteScalar() as string;
    }

    public void InsertPalette(string id, string json)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO palettes (id, json, updated_at) VALUES (@id, @json, @updatedAt)";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@json", json);
        cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public bool UpdatePalette(string id, string json)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE palettes SET json = @json, updated_at = @updatedAt WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@json", json);
        cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("o"));
        return cmd.ExecuteNonQuery() > 0;
    }

    /// <summary>Deletes a palette unless an app version is assigned to it (one statement, so no race with an assignment).</summary>
    public PaletteDeleteResult DeletePalette(string id)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM palettes
            WHERE id = @id AND NOT EXISTS (SELECT 1 FROM palette_assignments WHERE palette_id = @id)
            """;
        cmd.Parameters.AddWithValue("@id", id);
        if (cmd.ExecuteNonQuery() > 0)
            return PaletteDeleteResult.Deleted;

        return GetPaletteJson(id) is null ? PaletteDeleteResult.NotFound : PaletteDeleteResult.InUse;
    }

    public IReadOnlyList<(string AppVersion, string PaletteId)> GetPaletteAssignments()
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT app_version, palette_id FROM palette_assignments";
        using var reader = cmd.ExecuteReader();
        var rows = new List<(string, string)>();
        while (reader.Read())
            rows.Add((reader.GetString(0), reader.GetString(1)));
        return rows;
    }

    /// <summary>Each assigned app version with the JSON of its palette.</summary>
    public IReadOnlyList<(string AppVersion, string Json)> GetAssignedPaletteJson()
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT a.app_version, p.json
            FROM palette_assignments a
            JOIN palettes p ON p.id = a.palette_id
            """;
        using var reader = cmd.ExecuteReader();
        var rows = new List<(string, string)>();
        while (reader.Read())
            rows.Add((reader.GetString(0), reader.GetString(1)));
        return rows;
    }

    public void AssignPalette(string appVersion, string paletteId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO palette_assignments (app_version, palette_id) VALUES (@appVersion, @paletteId)
            ON CONFLICT (app_version) DO UPDATE SET palette_id = EXCLUDED.palette_id
            """;
        cmd.Parameters.AddWithValue("@appVersion", appVersion);
        cmd.Parameters.AddWithValue("@paletteId", paletteId);
        cmd.ExecuteNonQuery();
    }

    public bool UnassignPalette(string appVersion)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM palette_assignments WHERE app_version = @appVersion";
        cmd.Parameters.AddWithValue("@appVersion", appVersion);
        return cmd.ExecuteNonQuery() > 0;
    }
}
