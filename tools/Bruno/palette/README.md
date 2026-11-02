# Palette collection (Bruno)

Open this folder in Bruno (**Open Collection**; it is in Bruno's YAML format, like the other collections here), pick the `staging` or `local` environment, and fill in:

- `username` and `password` for a normal app account on that server.
- `adminToken` (staging: the server's `BearerToken`; Bruno keeps the secret values on your machine, not in these files).

Then **Run** the collection. Steps run in order and each has assertions:

1. Login.
2. A low-contrast palette is rejected (400); 3. a palette with no name is rejected (400).
4. Create a palette (201); 5. list the palettes.
6. A phone on `testAppVersion` does not see it yet (saved, not assigned).
7. Assign it to `testAppVersion`; 8. that version now gets it; 9. another version does not.
10. Update it; 11. the assigned version sees the edit (editing an assigned palette is live).
12. Deleting it while assigned is refused (409).
13. Unassign it; 14. delete it (204); 15. a bad admin token gets a 401.

`testAppVersion` is `9.9.9`, a version no real build has, so real phones never see the test palette. The requests that
act as a phone send `X-App-Version: 9.9.9 (1)` (or `1.0.0 (1)` for "another version").

Staging and Production share one database. If a run stops part way, run **13 Unassign palette** and **14 Delete
palette** so nothing is left assigned.
