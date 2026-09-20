using Microsoft.UI.Xaml.Controls;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Connections;
using TunnelDeck.Core.Models;

namespace TunnelDeck.ViewModels;

public sealed partial class ServerDetailViewModel
{
    private CancellationTokenSource? _testingCancellation;
    private bool _isTesting;
    private string _testTitle = string.Empty;
    private string _testMessage = string.Empty;
    private string _fingerprint = string.Empty;
    private InfoBarSeverity _testSeverity = InfoBarSeverity.Informational;

    public bool IsTesting { get => _isTesting; private set { if (SetProperty(ref _isTesting, value)) { RefreshAvailability(); OnPropertyChanged(nameof(TestActionLabel)); } } }
    public string TestActionLabel => IsTesting ? "Cancel test" : "Test connection";
    public string TestTitle { get => _testTitle; private set { if (SetProperty(ref _testTitle, value)) { OnPropertyChanged(nameof(HasTestResult)); } } }
    public string TestMessage { get => _testMessage; private set => SetProperty(ref _testMessage, value); }
    public string Fingerprint { get => _fingerprint; private set => SetProperty(ref _fingerprint, value); }
    public bool HasTestResult => !string.IsNullOrEmpty(TestTitle);
    public InfoBarSeverity TestSeverity { get => _testSeverity; private set => SetProperty(ref _testSeverity, value); }

    public async Task TestOrCancelAsync()
    {
        if (IsTesting) { _testingCancellation?.Cancel(); TestTitle = "Cancelling connection test…"; return; }
        if (!CanTestAction) { return; }
        RemoteServer? candidate = BuildCandidate();
        if (candidate is null) { return; }
        bool useSaved = RememberCredential && CanUseSavedCredential;
        if (!IsPrivateKey && Secret.Length == 0 && !useSaved)
        {
            _credentialError = "Enter the SSH password to test this connection.";
            RefreshFieldErrors();
            return;
        }
        candidate = candidate with { CredentialReference = useSaved ? candidate.CredentialReference : null };
        ConnectionCredentials? credentials = Secret.Length > 0 ? new(Secret) : useSaved ? null : new(null);
        using var cancellation = new CancellationTokenSource();
        _testingCancellation = cancellation;
        IsTesting = true;
        Fingerprint = string.Empty;
        TestSeverity = InfoBarSeverity.Informational;
        TestTitle = "Connecting…";
        TestMessage = "The test uses these form values. Server edits are saved only when you select Save.";
        var progress = new Progress<ConnectionStage>(stage =>
        {
            if (!_active || !ReferenceEquals(_testingCancellation, cancellation) || cancellation.IsCancellationRequested) { return; }
            TestTitle = stage switch
            {
                ConnectionStage.ReadingCredentials => "Reading credentials…",
                ConnectionStage.AwaitingHostApproval => "Waiting for host-key confirmation…",
                ConnectionStage.VerifyingHostKey => "Verifying the trusted host and authenticating…",
                ConnectionStage.ClosingTest => "Closing the test connection…",
                _ => "Connecting…",
            };
        });
        try
        {
            ConnectionTestResult result = await _shell.TestConnectionAsync(candidate, credentials, progress, cancellation.Token);
            if (!_active) { return; }
            TestTitle = "Connection successful";
            TestMessage = "Authentication succeeded. The temporary test connection has been closed.";
            TestSeverity = InfoBarSeverity.Success;
            Fingerprint = $"Verified host key · {result.HostKey.Algorithm}\n{result.HostKey.Fingerprint}";
        }
        catch (OperationCanceledException)
        {
            if (!_active) { return; }
            TestTitle = "Connection test cancelled";
            TestMessage = "You can edit the form or try again.";
        }
        catch (ConnectionException exception)
        {
            if (!_active) { return; }
            TestTitle = "Connection failed";
            TestMessage = exception.Message;
            TestSeverity = InfoBarSeverity.Error;
            if (useSaved && exception.Failure is ConnectionFailure.CredentialRequired or ConnectionFailure.CredentialUnavailable)
            {
                _savedCredentialMissing = true;
                OnPropertyChanged(nameof(SecretHint));
            }
            if (exception.Failure is ConnectionFailure.CredentialRequired or ConnectionFailure.CredentialUnavailable or ConnectionFailure.AuthenticationFailed or ConnectionFailure.KeyUnlockFailed)
            {
                _credentialError = exception.Message;
            }
            if (exception.Failure == ConnectionFailure.KeyFileUnreadable) { _keyFileError = exception.Message; }
            RefreshFieldErrors();
        }
        catch (Exception)
        {
            if (!_active) { return; }
            TestTitle = "Connection failed";
            TestMessage = "The connection test could not finish. Check the form and try again.";
            TestSeverity = InfoBarSeverity.Error;
        }
        finally { _testingCancellation = null; IsTesting = false; }
    }

    private void ClearTestResult()
    {
        if (IsTesting) { return; }
        TestTitle = string.Empty;
        TestMessage = string.Empty;
        Fingerprint = string.Empty;
    }
}
