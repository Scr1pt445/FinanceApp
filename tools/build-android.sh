#!/usr/bin/env bash
# Builds a debug APK of the Android app (ADR 0013) into artifacts/android/FinanceApp-debug.apk.
# Needs the .NET MAUI Android workload (dotnet workload install maui-android), an Android SDK and JDK 17+;
# see README, section «Android». The APK is signed with the local debug key: install it on your own phone.
# Usage: tools/build-android.sh [android-sdk-dir]   (default: $ANDROID_HOME, or wherever .NET finds one)
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$root/src/FinanceApp.Mobile/FinanceApp.Mobile.csproj"
output="$root/artifacts/android"
sdk="${1:-${ANDROID_HOME:-}}"

# Debug builds normally leave the assemblies out of the APK and push them from the IDE (fast deployment);
# an APK copied to a phone by hand must carry them.
args=(build "$project" --configuration Debug -p:EmbedAssembliesIntoApk=true)
[[ -n "$sdk" ]] && args+=("-p:AndroidSdkDirectory=$sdk")
[[ -n "${JAVA_HOME:-}" ]] && args+=("-p:JavaSdkDirectory=$JAVA_HOME")

dotnet "${args[@]}"

apk="$(find "$root/src/FinanceApp.Mobile/bin/Debug/net10.0-android" -name '*-Signed.apk' | head -n 1)"
[[ -n "$apk" ]] || { echo "APK не найден." >&2; exit 1; }
mkdir -p "$output"
cp "$apk" "$output/FinanceApp-debug.apk"
echo "Готово: $output/FinanceApp-debug.apk ($(( $(wc -c < "$apk") / 1024 / 1024 )) МБ)"
