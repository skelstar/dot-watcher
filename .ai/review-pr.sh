#!/bin/bash

# AI PR Review Helper Script
# Usage: ./.ai/review-pr.sh [target-branch]

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
    echo -e "${CYAN}PR Review Setup${RESET}"
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
echo -e "${BLUE}PR Review Request${RESET}"
echo -e "${BLUE}============================================${RESET}"
echo -e "${GREEN}Source: ${BOLD}$SOURCE_BRANCH${RESET}"
echo -e "${GREEN}Target: ${BOLD}$TARGET_BRANCH${RESET}"
echo -e "${BLUE}============================================${RESET}"
echo ""
echo -e "${MAGENTA}${BOLD}PROMPT START - Copy from here${RESET}"
echo -e "${CYAN}----------------------------------------${RESET}"
echo ""
echo "Please do a STATIC CODE REVIEW ONLY of the PR from branch '$SOURCE_BRANCH' to '$TARGET_BRANCH' using the template at .ai/pr-review-template.md."
echo ""
echo "Repository policy: do NOT run build, publish, test, package, simulator, server, Docker, or CI-equivalent commands unless I explicitly ask in this turn. GitHub Actions CI owns runtime verification. Use source-only inspection such as git diff, git status, rg, and file reads."
echo ""
echo "Generate a comprehensive review covering:"
echo "- Findings first, ordered by severity, with file/line references"
echo "- Correctness and edge cases"
echo "- API compatibility and existing URI stability"
echo "- Security and privacy implications for GPS/session data"
echo "- Performance, persistence, and deployment risks"
echo "- Static test coverage assessment"
echo "- Prioritized recommendations"
echo ""
echo "Write the output to: .ai/reviews/PR_REVIEW_${SOURCE_BRANCH//\//_}.md"
echo ""
echo -e "${CYAN}----------------------------------------${RESET}"
echo -e "${MAGENTA}${BOLD}PROMPT END - Copy to here${RESET}"
echo ""
echo -e "${YELLOW}Copy the text between the lines and paste into your AI coding assistant.${RESET}"
echo ""
