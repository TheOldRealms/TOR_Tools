#!/usr/bin/env bash
cd "$(dirname "$(readlink -f "$0")")"
exec ./release-linux/TORTools.App "$@"
