[CmdletBinding()]
param([Parameter(Mandatory)][long] $WindowHandle, [Parameter(Mandatory)][string] $AutomationId, [Parameter(Mandatory)][double] $Value)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$WindowHandle)
$condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $AutomationId)
$element = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
if (-not $element) { throw 'The requested native slider was not found.' }
$range = $element.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
if ($range.Current.IsReadOnly -or $Value -lt $range.Current.Minimum -or $Value -gt $range.Current.Maximum) { throw 'The requested slider value is not available.' }
$before = $range.Current.Value
$range.SetValue($Value)
if ([Math]::Abs($range.Current.Value - $Value) -gt 0.01) { throw 'The native range value did not change.' }
[ordered]@{ automationId = $AutomationId; before = $before; after = $range.Current.Value; minimum = $range.Current.Minimum; maximum = $range.Current.Maximum } | ConvertTo-Json
