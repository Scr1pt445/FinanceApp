#!/usr/bin/env bash
# Builds the signed release APK of the Android app (ADR 0013) into artifacts/android/FinanceApp-<version>.apk.
# The signing key never goes into the repository. Pass it through the environment:
#   FINANCEAPP_KEYSTORE           path to the .jks file
#   FINANCEAPP_KEY_ALIAS          the key's alias in it
#   FINANCEAPP_KEYSTORE_PASSWORD  the password of the keystore (and of the key)
# Every update must be signed with the same key, or Android refuses to install it over the old version: keep the
# keystore and its password in two safe places.
# Usage: tools/build-android-release.sh [android-sdk-dir]   (default: $ANDROID_HOME, or wherever .NET finds one)
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$root/src/FinanceApp.Mobile/FinanceApp.Mobile.csproj"
output="$root/artifacts/android"
sdk="${1:-${ANDROID_HOME:-}}"

for name in FINANCEAPP_KEYSTORE FINANCEAPP_KEY_ALIAS FINANCEAPP_KEYSTORE_PASSWORD; do
  [[ -n "${!name:-}" ]] || { echo "Не задана переменная $name (см. начало скрипта)." >&2; exit 1; }
done
[[ -f "$FINANCEAPP_KEYSTORE" ]] || { echo "Файл ключа не найден: $FINANCEAPP_KEYSTORE" >&2; exit 1; }

# The passwords are read by MSBuild from the environment (env:NAME), so they stay out of the command line and logs.
args=(publish "$project" --framework net10.0-android --configuration Release
  -p:AndroidPackageFormat=apk
  -p:AndroidKeyStore=true
  "-p:AndroidSigningKeyStore=$FINANCEAPP_KEYSTORE"
  "-p:AndroidSigningKeyAlias=$FINANCEAPP_KEY_ALIAS"
  -p:AndroidSigningStorePass=env:FINANCEAPP_KEYSTORE_PASSWORD
  -p:AndroidSigningKeyPass=env:FINANCEAPP_KEYSTORE_PASSWORD
  --output "$root/artifacts/android/publish")
[[ -n "$sdk" ]] && args+=("-p:AndroidSdkDirectory=$sdk")
[[ -n "${JAVA_HOME:-}" ]] && args+=("-p:JavaSdkDirectory=$JAVA_HOME")

dotnet "${args[@]}"

apk="$(find "$root/artifacts/android/publish" -name '*-Signed.apk' | head -n 1)"
[[ -n "$apk" ]] || { echo "APK не найден." >&2; exit 1; }
version="$(grep -oP '(?<=<Version>)[^<]+' "$root/Directory.Build.props")"
mkdir -p "$output"
cp "$apk" "$output/FinanceApp-$version.apk"
rm -rf "$root/artifacts/android/publish"
echo "Готово: $output/FinanceApp-$version.apk ($(( $(wc -c < "$output/FinanceApp-$version.apk") / 1024 / 1024 )) МБ)"
