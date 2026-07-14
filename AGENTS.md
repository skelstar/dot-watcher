# Repository Instructions

Validation is delegated to GitHub Actions CI. The server workflow runs `dotnet restore`, `dotnet build`, `dotnet publish`, and discovered .NET test projects on pull requests.

Allowed local commands include read-only inspection commands and targeted file edits. If a change appears to need build, publish, or test verification, leave that verification to CI and mention it in the final response.
