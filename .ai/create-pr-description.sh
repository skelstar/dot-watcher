#!/bin/bash

# AI PR Description Helper Script
# Usage: ./.ai/create-pr-description.sh [target-branch]

BLUE='\033[0;34m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
MAGENTA='\033[0;35m'
BOLD='\033[1m'
RESET='\033[0m'

SOURCE_BRANCH=$(git branch --show-current)

if [ -n "$1" ]; then
    TARGET_BRANCH="$1"
else
    echo -e "${CYAN}============================================${RESET}"
    echo -e "${CYAN}PR Description Setup${RESET}"
    echo -e "${CYAN}============================================${RESET}"
    echo ""
    echo -e "${GREEN}Source branch: ${BOLD}$SOURCE_BRANCH${RESET} ${GREEN}(current)${RESET}"
    echo ""
    echo -e "${YELLOW}Common target branches:${RESET}"
    echo "  1) main"
    echo "  2) develop"
    echo "  3) Other"
    echo ""
    read -r -p "Select target branch [1-3] (default: 1): " choice

    case $choice in
        2)
            TARGET_BRANCH="develop"
            ;;
        3)
            read -r -p "Enter target branch name: " TARGET_BRANCH
            ;;
        *)
            TARGET_BRANCH="main"
            ;;
    esac

    echo ""
fi

echo -e "${BLUE}============================================${RESET}"
echo -e "${BLUE}PR Description Request${RESET}"
echo -e "${BLUE}============================================${RESET}"
echo -e "${GREEN}Source: ${BOLD}$SOURCE_BRANCH${RESET}"
echo -e "${GREEN}Target: ${BOLD}$TARGET_BRANCH${RESET}"
echo -e "${BLUE}============================================${RESET}"
echo ""
echo -e "${MAGENTA}${BOLD}PROMPT START - Copy from here${RESET}"
echo -e "${CYAN}----------------------------------------${RESET}"
echo ""
echo "Please create a PR title and description for the changes from branch '$SOURCE_BRANCH' to '$TARGET_BRANCH'."
echo ""
echo "Repository policy: do NOT run build, publish, test, package, simulator, server, Docker, or CI-equivalent commands unless I explicitly ask in this turn. GitHub Actions CI owns runtime verification. Use source-only inspection such as git diff, git status, git log, rg, and file reads."
echo ""
echo "Generate:"
echo "1. A concise PR title, 60 characters max, following format: 'Category: Brief description'"
echo "2. A markdown PR description including:"
echo "   - Summary"
echo "   - Key changes with file references"
echo "   - User/product impact"
echo "   - API/auth/session compatibility notes"
echo "   - Security and privacy notes for GPS/session data"
echo "   - Migration/deployment notes"
echo "   - Testing or verification notes, clearly distinguishing source checks from GitHub Actions/runtime verification"
echo "   - Outstanding TODOs: list open TODO.md entries relevant to this PR if TODO.md exists, otherwise write 'None found'"
echo "   - Risks or follow-up work"
echo ""
echo "Write the output to: .ai/pr-descriptions/PR_DESC_${SOURCE_BRANCH//\//_}.md"
echo ""
echo "Output format:"
echo "# PR Title"
echo "[Generated title]"
echo ""
echo "# PR Description"
echo "[Generated markdown description]"
echo ""
echo -e "${CYAN}----------------------------------------${RESET}"
echo -e "${MAGENTA}${BOLD}PROMPT END - Copy to here${RESET}"
echo ""
echo -e "${YELLOW}Copy the text between the lines and paste into your AI coding assistant.${RESET}"
echo ""
