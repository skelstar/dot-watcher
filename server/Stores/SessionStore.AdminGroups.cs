using Npgsql;
using NpgsqlTypes;

namespace DotWatcher.Server;

// The admin page's Groups tab (see .ai/plans/go-and-groups.md): every named session is a group for now, so
// these list and change them directly, with no owner check. The casual and demo sessions are never groups.
public partial class SessionStore
{
    private const string AdminGroupColumns = """
        s.id, s.session_name, s.invite_code, s.owner_user_id, u.username, u.display_name,
        (SELECT COUNT(*) FROM session_members m WHERE m.session_id = s.id AND m.left_at IS NULL),
        s.created_at, s.max_length_hours, s.archived_at, s.nearby, s.nearby_radius_m,
        s.anchor_lat, s.anchor_lon, s.entry_code
        """;

    /// <summary>Every group, newest first, with its status as of <paramref name="now"/>.</summary>
    public IReadOnlyList<AdminGroupSummary> GetAdminGroups(DateTimeOffset now)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT {AdminGroupColumns}
            FROM app_sessions s
            JOIN users u ON u.id = s.owner_user_id
            WHERE s.id <> @casualSessionId AND s.id <> @demoSessionId
            ORDER BY s.created_at DESC
            """;
        cmd.Parameters.AddWithValue("@casualSessionId", CasualSessionId);
        cmd.Parameters.AddWithValue("@demoSessionId", DemoSessionId ?? "");
        using var reader = cmd.ExecuteReader();
        var groups = new List<AdminGroupSummary>();
        while (reader.Read())
            groups.Add(ReadAdminGroup(reader, now));
        return groups;
    }

    /// <summary>One group, or null when there's no such group (or it's the casual or demo session).</summary>
    public AdminGroupSummary? GetAdminGroup(string sessionId, DateTimeOffset now)
    {
        if (!IsAdminGroupId(sessionId))
            return null;

        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT {AdminGroupColumns}
            FROM app_sessions s
            JOIN users u ON u.id = s.owner_user_id
            WHERE s.id = @sessionId
            """;
        cmd.Parameters.AddWithValue("@sessionId", sessionId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadAdminGroup(reader, now) : null;
    }

    private static AdminGroupSummary ReadAdminGroup(NpgsqlDataReader reader, DateTimeOffset now)
    {
        var createdAt = reader.GetString(7);
        int? storedLength = reader.IsDBNull(8) ? null : reader.GetInt16(8);
        var endsAt = GroupEndsAt(DateTimeOffset.Parse(createdAt), storedLength);
        var status = !reader.IsDBNull(9) ? "ended" : endsAt > now ? "active" : "expired";
        return new AdminGroupSummary(
            SessionId: reader.GetString(0),
            SessionName: reader.GetString(1),
            InviteCode: reader.GetString(2),
            OwnerUserId: reader.GetString(3),
            OwnerUsername: reader.GetString(4),
            OwnerDisplayName: reader.GetString(5),
            MemberCount: (int)reader.GetInt64(6),
            CreatedAt: createdAt,
            LengthHours: storedLength ?? DefaultMaxLengthHours,
            EndsAt: endsAt.ToString("O"),
            Status: status,
            Nearby: reader.GetBoolean(10),
            NearbyRadiusMeters: reader.IsDBNull(11) ? null : reader.GetInt16(11),
            JoinLatitude: reader.IsDBNull(12) ? null : reader.GetDouble(12),
            JoinLongitude: reader.IsDBNull(13) ? null : reader.GetDouble(13),
            EntryCode: reader.IsDBNull(14) ? null : reader.GetString(14));
    }

    /// <summary>Everyone who has been in the group, current members first, then by when they joined.</summary>
    public IReadOnlyList<AdminGroupMember> GetAdminGroupMembers(string sessionId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT m.user_id, u.username, m.display_name, m.role, m.joined_at, m.left_at
            FROM session_members m
            JOIN users u ON u.id = m.user_id
            WHERE m.session_id = @sessionId
            ORDER BY (m.left_at IS NOT NULL), m.joined_at
            """;
        cmd.Parameters.AddWithValue("@sessionId", sessionId);
        using var reader = cmd.ExecuteReader();
        var members = new List<AdminGroupMember>();
        while (reader.Read())
        {
            members.Add(new AdminGroupMember(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
        }
        return members;
    }

    /// <summary>
    /// Applies an admin's already-validated changes to a group. Each null field is left as it is. Turning
    /// nearby on needs a join location, given here or stored already: NeedsJoinLocation otherwise.
    /// </summary>
    public AdminGroupUpdateOutcome UpdateAdminGroup(string sessionId, AdminGroupChanges changes)
    {
        if (!IsAdminGroupId(sessionId))
            return AdminGroupUpdateOutcome.NotFound;

        using var conn = Connect();
        bool hasJoinLocation;
        using (var read = conn.CreateCommand())
        {
            read.CommandText = "SELECT anchor_lat IS NOT NULL FROM app_sessions WHERE id = @sessionId";
            read.Parameters.AddWithValue("@sessionId", sessionId);
            if (read.ExecuteScalar() is not bool stored)
                return AdminGroupUpdateOutcome.NotFound;
            hasJoinLocation = stored;
        }

        if (changes.Nearby == true && !hasJoinLocation && changes.JoinLatitude is null)
            return AdminGroupUpdateOutcome.NeedsJoinLocation;

        using var update = conn.CreateCommand();
        update.CommandText = """
            UPDATE app_sessions
            SET session_name = COALESCE(@name, session_name),
                max_length_hours = COALESCE(@length, max_length_hours),
                nearby = COALESCE(@nearby, nearby),
                nearby_radius_m = COALESCE(@radius, nearby_radius_m),
                anchor_lat = COALESCE(@lat, anchor_lat),
                anchor_lon = COALESCE(@lon, anchor_lon),
                anchor_accuracy_m = CASE WHEN @lat IS NULL THEN anchor_accuracy_m ELSE NULL END,
                entry_code = CASE WHEN @clearCode THEN NULL ELSE COALESCE(@code, entry_code) END
            WHERE id = @sessionId
            """;
        update.Parameters.AddWithValue("@name", NpgsqlDbType.Text, (object?)changes.SessionName ?? DBNull.Value);
        update.Parameters.AddWithValue("@length", NpgsqlDbType.Smallint,
            changes.LengthHours is int length ? (short)length : DBNull.Value);
        update.Parameters.AddWithValue("@nearby", NpgsqlDbType.Boolean, (object?)changes.Nearby ?? DBNull.Value);
        update.Parameters.AddWithValue("@radius", NpgsqlDbType.Smallint,
            changes.NearbyRadiusMeters is int radius ? (short)radius : DBNull.Value);
        update.Parameters.AddWithValue("@lat", NpgsqlDbType.Double, (object?)changes.JoinLatitude ?? DBNull.Value);
        update.Parameters.AddWithValue("@lon", NpgsqlDbType.Double, (object?)changes.JoinLongitude ?? DBNull.Value);
        update.Parameters.AddWithValue("@clearCode", changes.ClearEntryCode);
        update.Parameters.AddWithValue("@code", NpgsqlDbType.Text, (object?)changes.EntryCode ?? DBNull.Value);
        update.Parameters.AddWithValue("@sessionId", sessionId);
        update.ExecuteNonQuery();
        return AdminGroupUpdateOutcome.Ok;
    }

    /// <summary>Ends a group for everyone, as its organiser's End group does. False when there's no such group.</summary>
    public bool AdminEndGroup(string sessionId)
    {
        if (!IsAdminGroupId(sessionId))
            return false;

        using var conn = Connect();
        using var tx = conn.BeginTransaction();
        using (var exists = conn.CreateCommand())
        {
            exists.Transaction = tx;
            exists.CommandText = "SELECT 1 FROM app_sessions WHERE id = @sessionId";
            exists.Parameters.AddWithValue("@sessionId", sessionId);
            if (exists.ExecuteScalar() is null)
                return false;
        }

        ArchiveAndEmpty(conn, tx, sessionId, DateTimeOffset.UtcNow.ToString("O"));
        tx.Commit();
        _sessions.TryRemove(sessionId, out _);
        return true;
    }

    /// <summary>
    /// Undoes an end: the group is no longer archived, so people can join it again. Members who left stay
    /// out (add them back one by one). It still stops at its end time. False when there's no such group.
    /// </summary>
    public bool AdminReopenGroup(string sessionId)
    {
        if (!IsAdminGroupId(sessionId))
            return false;

        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE app_sessions SET archived_at = NULL WHERE id = @sessionId";
        cmd.Parameters.AddWithValue("@sessionId", sessionId);
        return cmd.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// Puts an account in a group (or back in, after leaving), as a nearby join would, without the
    /// distance, entry code or block checks. <paramref name="displayName"/> null uses the account's own.
    /// </summary>
    public AdminAddMemberOutcome AdminAddGroupMember(string sessionId, string username, string? displayName)
    {
        if (!IsAdminGroupId(sessionId))
            return AdminAddMemberOutcome.NoGroup;

        var user = GetUserByUsername(username);
        if (user is null)
            return AdminAddMemberOutcome.NoUser;

        using var conn = Connect();
        using (var read = conn.CreateCommand())
        {
            read.CommandText = "SELECT archived_at IS NOT NULL FROM app_sessions WHERE id = @sessionId";
            read.Parameters.AddWithValue("@sessionId", sessionId);
            switch (read.ExecuteScalar())
            {
                case null:
                    return AdminAddMemberOutcome.NoGroup;
                case true:
                    return AdminAddMemberOutcome.Ended;
            }
        }

        UpsertMembership(conn, sessionId, user.Id, "runner", displayName ?? user.DisplayName,
            DateTimeOffset.UtcNow.ToString("O"));
        return AdminAddMemberOutcome.Ok;
    }

    private bool IsAdminGroupId(string sessionId) =>
        !string.IsNullOrWhiteSpace(sessionId) && !IsCasualSession(sessionId) && !IsDemoSession(sessionId);
}

public enum AdminGroupUpdateOutcome { Ok, NotFound, NeedsJoinLocation }

public enum AdminAddMemberOutcome { Ok, NoGroup, NoUser, Ended }

/// <summary>Validated changes to a group from the admin page; null leaves a field as it is.</summary>
public record AdminGroupChanges(
    string? SessionName = null,
    int? LengthHours = null,
    bool? Nearby = null,
    int? NearbyRadiusMeters = null,
    double? JoinLatitude = null,
    double? JoinLongitude = null,
    string? EntryCode = null,
    bool ClearEntryCode = false);
