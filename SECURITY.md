# Security Policy

## Supported versions

Only the latest version listed in PluginHub-SE2 is supported. See the [changelog](CHANGELOG.md).

## What the plugin does and does not do

- It runs inside the game as a Pulsar plugin, compiled from this source on your machine.
- It makes **no network connections**.
- It reads and writes only the game log, its own settings file, and a diagnostic report that is written
  only when you press the button for it in the settings.

A change that breaks any of these points is a security problem. Please report it.

## Reporting a vulnerability

Please do **not** open a public issue for a security problem.

Use GitHub's private reporting: the **Security** tab of this repository, then **Report a vulnerability**.
Include the plugin version, what you found, and how to reproduce it.

This is a hobby project with one maintainer. Expect a first answer within about a week.
