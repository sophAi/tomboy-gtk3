#!/usr/bin/env bash
# ==============================================================================
# Script: build_deb.sh
# Description: Compiles and packages Tomboy (.NET 8 + GTK3 + LaTeX)
#              into a standalone Debian (.deb) package.
# Supported OS: Linux Mint 21 / 22 / 23, Ubuntu 22.04 / 24.04 / 26.04 (amd64)
# ==============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUTPUT_DEB="${1:-$SCRIPT_DIR/tomboy_gtk3_2.0.0_amd64.deb}"
STAGING_DIR="/tmp/tomboy-deb-staging-$$"

VERSION="2.0.0-gtk3"
ARCH="amd64"

echo "=========================================================="
echo "  Tomboy (.NET 8 + GTK3 + LaTeX) Debian Package Builder"
echo "=========================================================="
echo "Repository Root : $SCRIPT_DIR"
echo "Output Package  : $OUTPUT_DEB"
echo "Staging Temp    : $STAGING_DIR"
echo "=========================================================="

# 1. Check Prerequisites
echo "[1/6] Checking prerequisites..."
if ! command -v dotnet >/dev/null 2>&1; then
    echo "ERROR: 'dotnet' command not found."
    echo "Please install .NET SDK (e.g. 'sudo apt install dotnet-sdk-8.0')."
    exit 1
fi

if ! command -v dpkg-deb >/dev/null 2>&1; then
    echo "ERROR: 'dpkg-deb' command not found."
    echo "Please install dpkg tools (e.g. 'sudo apt install dpkg')."
    exit 1
fi

DOTNET_VER="$(dotnet --version)"
echo "Found .NET SDK version: $DOTNET_VER"

if [ ! -f "$SCRIPT_DIR/src/Tomboy/Tomboy.csproj" ]; then
    echo "ERROR: Tomboy project file not found at: $SCRIPT_DIR/src/Tomboy/Tomboy.csproj"
    exit 1
fi

if [ ! -f "$SCRIPT_DIR/src/Tomboy.Latex/Tomboy.Latex.csproj" ]; then
    echo "ERROR: Tomboy.Latex project file not found at: $SCRIPT_DIR/src/Tomboy.Latex/Tomboy.Latex.csproj"
    exit 1
fi

# 2. Prepare staging directory
echo "[2/6] Preparing staging directory..."
rm -rf "$STAGING_DIR"
mkdir -p "$STAGING_DIR/DEBIAN"
mkdir -p "$STAGING_DIR/usr/bin"
mkdir -p "$STAGING_DIR/usr/lib/tomboy"
mkdir -p "$STAGING_DIR/usr/share/applications"
mkdir -p "$STAGING_DIR/usr/share/dbus-1/services"

# 3. Compile and Publish Release Build
echo "[3/6] Compiling and publishing self-contained Release build..."
dotnet publish "$SCRIPT_DIR/src/Tomboy/Tomboy.csproj" \
    -c Release \
    -r linux-x64 \
    --self-contained \
    -o "$STAGING_DIR/usr/lib/tomboy/"

if [ ! -f "$STAGING_DIR/usr/lib/tomboy/Tomboy" ]; then
    echo "ERROR: Build failed, executable $STAGING_DIR/usr/lib/tomboy/Tomboy not found."
    rm -rf "$STAGING_DIR"
    exit 1
fi

chmod +x "$STAGING_DIR/usr/lib/tomboy/Tomboy"

# 4. Install Desktop Integrations and Icons
echo "[4/6] Installing desktop integrations and application icons..."

# Symlink executable to /usr/bin/tomboy
ln -sf /usr/lib/tomboy/Tomboy "$STAGING_DIR/usr/bin/tomboy"

# Desktop Entry
if [ -f "$SCRIPT_DIR/data/tomboy.desktop" ]; then
    cp "$SCRIPT_DIR/data/tomboy.desktop" "$STAGING_DIR/usr/share/applications/tomboy.desktop"
else
    cat << 'EOF' > "$STAGING_DIR/usr/share/applications/tomboy.desktop"
[Desktop Entry]
Name=Tomboy Notes
Comment=Desktop note-taking application (.NET 8 + GTK3)
Exec=/usr/bin/tomboy %F
Icon=tomboy
Terminal=false
Type=Application
Categories=GNOME;GTK;Utility;TextEditor;
StartupNotify=true
MimeType=application/x-note;
EOF
fi
chmod 644 "$STAGING_DIR/usr/share/applications/tomboy.desktop"

# D-Bus Service File
if [ -f "$SCRIPT_DIR/data/org.gnome.Tomboy.service" ]; then
    cp "$SCRIPT_DIR/data/org.gnome.Tomboy.service" "$STAGING_DIR/usr/share/dbus-1/services/org.gnome.Tomboy.service"
else
    cat << 'EOF' > "$STAGING_DIR/usr/share/dbus-1/services/org.gnome.Tomboy.service"
[D-BUS Service]
Name=org.gnome.Tomboy
Exec=/usr/bin/tomboy
EOF
fi
chmod 644 "$STAGING_DIR/usr/share/dbus-1/services/org.gnome.Tomboy.service"

# Install Hicolor Icons
ICON_SRC="$SCRIPT_DIR/data/icons"
if [ -d "$ICON_SRC" ]; then
    # Scalable SVG
    if [ -f "$ICON_SRC/hicolor_apps_scalable_tomboy.svg" ]; then
        mkdir -p "$STAGING_DIR/usr/share/icons/hicolor/scalable/apps"
        cp "$ICON_SRC/hicolor_apps_scalable_tomboy.svg" "$STAGING_DIR/usr/share/icons/hicolor/scalable/apps/tomboy.svg"
    fi

    # Fixed Size PNGs
    for SIZE in 16 22 24 32 48 256; do
        SRC_PNG="$ICON_SRC/hicolor_apps_${SIZE}x${SIZE}_tomboy.png"
        if [ -f "$SRC_PNG" ]; then
            mkdir -p "$STAGING_DIR/usr/share/icons/hicolor/${SIZE}x${SIZE}/apps"
            cp "$SRC_PNG" "$STAGING_DIR/usr/share/icons/hicolor/${SIZE}x${SIZE}/apps/tomboy.png"
        fi
    done
fi

# 5. Generate Debian Control Scripts
echo "[5/6] Generating Debian package control scripts..."

cat << EOF > "$STAGING_DIR/DEBIAN/control"
Package: tomboy
Version: $VERSION
Section: utils
Priority: optional
Architecture: $ARCH
Maintainer: Tomboy Maintainers <tomboy-dev@gnome.org>
Depends: libgtk-3-0 | libgtk-3-0t64, libglib2.0-0 | libglib2.0-0t64, dconf-gsettings-backend
Recommends: texlive-latex-base, dvipng
Description: Desktop note-taking application (.NET 8 + GTK3)
 Tomboy is a classic desktop note-taking application for Linux.
 .
 Modernized port using .NET 8 and GtkSharp (GTK3).
 Supports Linux Mint mate-panel System Tray Icon, right-click menu,
 Notebooks, XML indentations, HTML export, URL auto-hyperlinks,
 and LaTeX math formula rendering.
EOF

cat << 'EOF' > "$STAGING_DIR/DEBIAN/postinst"
#!/bin/sh
set -e

if [ "$1" = "configure" ]; then
    if [ -x /usr/bin/glib-compile-schemas ]; then
        /usr/bin/glib-compile-schemas /usr/share/glib-2.0/schemas 2>/dev/null || true
    fi
    if [ -x /usr/bin/update-desktop-database ]; then
        /usr/bin/update-desktop-database -q /usr/share/applications 2>/dev/null || true
    fi
    if [ -x /usr/bin/gtk-update-icon-cache ]; then
        /usr/bin/gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor 2>/dev/null || true
    fi
fi

exit 0
EOF
chmod 755 "$STAGING_DIR/DEBIAN/postinst"

cat << 'EOF' > "$STAGING_DIR/DEBIAN/postrm"
#!/bin/sh
set -e

if [ "$1" = "remove" ] || [ "$1" = "purge" ]; then
    if [ -x /usr/bin/glib-compile-schemas ]; then
        /usr/bin/glib-compile-schemas /usr/share/glib-2.0/schemas 2>/dev/null || true
    fi
    if [ -x /usr/bin/update-desktop-database ]; then
        /usr/bin/update-desktop-database -q /usr/share/applications 2>/dev/null || true
    fi
    if [ -x /usr/bin/gtk-update-icon-cache ]; then
        /usr/bin/gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor 2>/dev/null || true
    fi
fi

exit 0
EOF
chmod 755 "$STAGING_DIR/DEBIAN/postrm"

# 6. Build DEB Package
echo "[6/6] Packaging into $OUTPUT_DEB..."
mkdir -p "$(dirname "$OUTPUT_DEB")"
dpkg-deb --build --root-owner-group "$STAGING_DIR" "$OUTPUT_DEB"

# Clean up staging directory
rm -rf "$STAGING_DIR"

echo "=========================================================="
echo "  Build & Packaging Successful!"
echo "=========================================================="
echo "Package File: $OUTPUT_DEB"
ls -lh "$OUTPUT_DEB"
echo ""
echo "To install on your system:"
echo "  sudo dpkg -i $OUTPUT_DEB"
echo "  sudo apt-get install -f   # If any dependencies are needed"
echo "=========================================================="
