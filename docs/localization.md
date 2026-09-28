# Interface languages

TunnelDeck supports English (`en-US`) and Simplified Chinese (`zh-CN`). Select **Settings → Appearance → Language** (设置 → 外观 → 语言). The selection is saved immediately and applied to the existing window after a successful save. New installations and migrated configurations default to English; the app does not select a language from Windows settings.

## Resource and binding model

`TunnelDeck.Core/Localization/Strings.resx` contains the English catalog and `Strings.zh-CN.resx` contains its Chinese counterpart. The .NET SDK builds the Chinese satellite assembly automatically. Keep that assembly when publishing or copying the application.

The shared `UiText` resource service uses an explicit culture, so worker-thread culture cannot change application messages. `LocalizedStrings` exposes a replaceable `Values` dictionary and a named property-change notification to native WinUI bindings:

```xaml
Text="{Binding Values[MainWindow_Settings], Source={StaticResource Strings}, Mode=OneWay}"
```

The window's `Language` property also follows the selection. View models and rows receive weak language notifications, update their presentation properties, and retain their runtime snapshots. Language switching does not rebuild the window, navigate the Frame, reset Quick forward values, or restart tunnels. Native title-bar and search-template labels are refreshed separately.

Use a live `ContentTemplate` for localized `ComboBoxItem` content. WinUI caches the selected item's string in its selection box; replacing only `ComboBoxItem.Content` can otherwise leave the previous language visible until the selection changes. AppBarButton `Label`, accessible names, tooltips, empty placeholders, and toggle On/Off labels also need resources.

## Data boundaries

App-owned backend diagnostics are created with `UiText.Invariant`, retaining English in persisted activity and logs. `UiText.Translate` translates known messages at the display boundary, including historical diagnostics. It matches complete catalog messages and anchored format templates, preserving substitution values unless a specific argument is another app-owned diagnostic. Pass neither arbitrary SSH output nor standalone user content to this method.

Server and profile names, hostnames, ports, paths, fingerprints, SSH keywords, identifiers, and protocol enums remain unchanged. State labels have separate invariant keys for status colors and icons. Relative times and counts use the selected language; timestamps remain UTC in storage and use the local time zone for display.

Schema 3 adds required `settings.language`. Schema 1 first receives the existing authentication-default migration, then schema 2 receives the explicit English language default. Each migration validates the old settings shape before adding fields. Loading does not write files; the first real edit uses the existing atomic save and preserves the old backup. Portable exports, host-key data, and credential-cleanup data remain schema 1.

## Focused verification

`LocalizationResourceTests` checks catalog parity, format arguments, XAML resource references, and unlocalized visible or accessible attributes, including command labels. `LocalizationTests` covers existing-row updates, counts and time formatting, validation and import diagnostics, preservation of user names and paths, and explicit language selection. `LanguagePersistenceTests` covers migration, strict validation, unchanged data, and reload.

The GUI check uses an isolated data directory and switches English → Chinese → English in one process. Check the existing sidebar, selected settings options, search placeholder, Dashboard usage placeholders, resource commands, import notes/footer, and a Quick forward draft. No real SSH connection or change to Windows language settings is needed.

The release ZIP produced on September 23 predates localization. This change is built as Release x64; that previous archive is retained unchanged.
