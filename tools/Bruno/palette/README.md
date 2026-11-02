# Palette collection (Bruno)

Open this folder in Bruno (**Open Collection**), pick the `staging` or `local` environment, and fill in:

- `username` and `password` for a normal app account on that server.
- `adminToken` (staging: the server's `BearerToken`; Bruno keeps the secret values on your machine, not in these files).

Then **Run** the collection. Steps run in order and each has assertions:

1. Login, then 2. read the palette apps see now.
3. A low-contrast palette is rejected (400); 4. a wrong admin token is rejected (401).
5. Set a good palette with a name and label (200); 6. read it back.
7. Clear it (204); 8. clearing again is a 404; 9. read it back, gone.

Staging and Production share one database. If a run stops part way, run **7 Clear palette** so a test palette
doesn't end up on Production.
