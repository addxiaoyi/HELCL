#!/usr/bin/env bash
set -euo pipefail

run_linux_session() {
    local verifier="$1" report="$2" scratch keyring_pid
    scratch="$(mktemp -d /tmp/hell-native-keyring.XXXXXX)"
    export XDG_DATA_HOME="$scratch/data" XDG_CONFIG_HOME="$scratch/config"
    export XDG_CACHE_HOME="$scratch/cache" XDG_RUNTIME_DIR="$scratch/runtime"
    export GNOME_KEYRING_CONTROL="$scratch/control"
    mkdir -p "$XDG_DATA_HOME" "$XDG_CONFIG_HOME" "$XDG_CACHE_HOME" "$XDG_RUNTIME_DIR" "$GNOME_KEYRING_CONTROL"
    chmod 700 "$scratch" "$XDG_RUNTIME_DIR" "$GNOME_KEYRING_CONTROL"
    keyring_pid=""
    cleanup_linux() {
        if [[ -n "$keyring_pid" ]]; then
            kill "$keyring_pid" 2>/dev/null || true
            wait "$keyring_pid" 2>/dev/null || true
        fi
        case "$scratch" in /tmp/hell-native-keyring.*) rm -rf -- "$scratch" ;; esac
    }
    trap cleanup_linux EXIT
    # This daemon has a private D-Bus session and private XDG storage; no login keyring is opened.
    printf '\n' | gnome-keyring-daemon --unlock --foreground --components=secrets \
        --control-directory="$GNOME_KEYRING_CONTROL" >"$scratch/daemon.stdout" 2>"$scratch/daemon.stderr" &
    keyring_pid=$!
    local ready=false
    for ((attempt = 0; attempt < 50; attempt++)); do
        if dbus-send --session --dest=org.freedesktop.DBus --type=method_call --print-reply \
            /org/freedesktop/DBus org.freedesktop.DBus.NameHasOwner string:org.freedesktop.secrets | grep -q 'boolean true'; then
            ready=true
            break
        fi
        sleep 0.1
    done
    if [[ "$ready" != true ]]; then
        echo 'The isolated native keyring did not start.' >&2
        return 1
    fi
    dotnet "$verifier" linux "$report"
    cleanup_linux
    trap - EXIT
}

if [[ "${1:-}" == --inside-linux ]]; then
    run_linux_session "$2" "$3"
    exit
fi

if [[ $# -lt 1 || $# -gt 2 || "$1" != /* ]]; then
    echo 'Usage: bash tools/run_native_credentials.sh /absolute/report.json [/absolute/artifacts-directory]' >&2
    exit 2
fi
repo="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
report="$1"
artifacts="${2:-$repo/artifacts/native-credentials}"
if [[ "$artifacts" != /* ]]; then echo 'The artifacts directory must be absolute.' >&2; exit 2; fi
dotnet build "$repo/tools/HelL.Credentials.Verify/HelL.Credentials.Verify.csproj" -c Release --artifacts-path "$artifacts" --nologo
verifier="$artifacts/bin/HelL.Credentials.Verify/release/HelL.Credentials.Verify.dll"
probe="$report.failure-probe.json"
if dotnet "$verifier" invalid-platform "$probe" >/dev/null 2>/dev/null; then
    echo 'Invalid verifier input unexpectedly succeeded.' >&2
    exit 1
fi
python3 - "$probe" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as stream:
    report = json.load(stream)
assert report["ok"] is False
assert report["stage"] == "arguments"
assert report["error_type"] == "ArgumentException"
assert report["completed_checks"] == []
assert report["failures"] == [{"stage": "arguments", "type": "ArgumentException"}]
assert report["native_entries_cleaned"] is False
assert report["temporary_files_cleaned"] is False
PY
rm -f -- "$probe"
echo 'Verifier failure-report contract passed.'

case "$(uname -s)" in
    Linux)
        command -v secret-tool >/dev/null
        command -v gnome-keyring-daemon >/dev/null
        command -v dbus-run-session >/dev/null
        exec dbus-run-session -- bash "$repo/tools/run_native_credentials.sh" --inside-linux "$verifier" "$report"
        ;;
    Darwin)
        if [[ "${GITHUB_ACTIONS:-}" != true && "${HELL_NATIVE_KEYCHAIN_ISOLATED:-}" != 1 ]]; then
            echo 'Use a dedicated macOS test account and set HELL_NATIVE_KEYCHAIN_ISOLATED=1.' >&2
            exit 2
        fi
        scratch="$(mktemp -d /tmp/hell-native-keychain.XXXXXX)"
        keychain="$scratch/verification.keychain-db"
        previous_default="$(security default-keychain -d user | python3 -c 'import sys; print(sys.stdin.read().strip().strip(chr(34)))')"
        previous_search="$(security list-keychains -d user)"
        previous_list=()
        while IFS= read -r entry; do previous_list+=("$entry"); done < <(
            printf '%s\n' "$previous_search" | python3 -c 'import sys; [print(line.strip().strip(chr(34))) for line in sys.stdin if line.strip()]'
        )
        cleanup_macos() {
            local cleanup_failed=0
            security default-keychain -d user -s "$previous_default" >/dev/null || cleanup_failed=1
            security list-keychains -d user -s "${previous_list[@]}" >/dev/null || cleanup_failed=1
            security delete-keychain "$keychain" >/dev/null || cleanup_failed=1
            case "$scratch" in /tmp/hell-native-keychain.*) rm -rf -- "$scratch" ;; esac
            if [[ "$cleanup_failed" != 0 ]]; then
                echo 'The temporary test keychain could not be fully cleaned up.' >&2
                exit 1
            fi
        }
        trap cleanup_macos EXIT
        # The disposable test keychain has an empty fixture password, never a user's password.
        security create-keychain -p '' "$keychain"
        security set-keychain-settings -lut 600 "$keychain"
        security unlock-keychain -p '' "$keychain"
        security list-keychains -d user -s "$keychain"
        security default-keychain -d user -s "$keychain"
        dotnet "$verifier" macos "$report"
        ;;
    *) echo 'This verifier requires Linux or macOS.' >&2; exit 2 ;;
esac
