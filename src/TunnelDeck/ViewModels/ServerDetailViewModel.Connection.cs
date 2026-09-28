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
    public string TestActionLabel => IsTesting ? UiText.Get("ServerDetailViewModelConnection_CancelTest") : UiText.Get("ServerDetailViewModelConnection_TestConnection");
    public string TestTitle { get => UiText.Translate(_testTitle); private set { if (SetProperty(ref _testTitle, value)) { OnPropertyChanged(nameof(HasTestResult)); } } }
    public string TestMessage { get => UiText.Translate(_testMessage); private set => SetProperty(ref _testMessage, value); }
    public string Fingerprint { get => UiText.Translate(_fingerprint); private set => SetProperty(ref _fingerprint, value); }
    public bool HasTestResult => !string.IsNullOrEmpty(TestTitle);
    public InfoBarSeverity TestSeverity { get => _testSeverity; private set => SetProperty(ref _testSeverity, value); }

    public async Task TestOrCancelAsync()
    {
        if (IsTesting) { _testingCancellation?.Cancel(); TestTitle = UiText.Get("ServerDetailViewModelConnection_CancellingConnectionTest"); return; }
        if (!CanTestAction) { return; }
        RemoteServer? candidate = BuildCandidate();
        if (candidate is null) { return; }
        bool useSaved = RememberCredential && CanUseSavedCredential;
        if (!IsPrivateKey && Secret.Length == 0 && !useSaved)
        {
            _credentialError = UiText.Get("ServerDetailViewModelConnection_EnterTheSSHPasswordToTestThisConnection");
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
        TestTitle = UiText.Get("ServerDetailViewModelConnection_Connecting");
        TestMessage = UiText.Get("ServerDetailViewModelConnection_TheTestUsesTheseFormValuesServerEdits");
        var progress = new Progress<ConnectionStage>(stage =>
        {
            if (!_active || !ReferenceEquals(_testingCancellation, cancellation) || cancellation.IsCancellationRequested) { return; }
            TestTitle = stage switch
            {
                ConnectionStage.ReadingCredentials => UiText.Get("ServerDetailViewModelConnection_ReadingCredentials"),
                ConnectionStage.AwaitingHostApproval => UiText.Get("ServerDetailViewModelConnection_WaitingForHostKeyConfirmation"),
                ConnectionStage.VerifyingHostKey => UiText.Get("ServerDetailViewModelConnection_VerifyingTheTrustedHostAndAuthenticating"),
                ConnectionStage.ClosingTest => UiText.Get("ServerDetailViewModelConnection_ClosingTheTestConnection"),
                _ => UiText.Get("ServerDetailViewModelConnection_Connecting"),
            };
        });
        try
        {
            ConnectionTestResult result = await _shell.TestConnectionAsync(candidate, credentials, progress, cancellation.Token);
            if (!_active) { return; }
            TestTitle = UiText.Get("ServerDetailViewModelConnection_ConnectionSuccessful");
            TestMessage = UiText.Get("ServerDetailViewModelConnection_AuthenticationSucceededTheTemporaryTestConnectionHasBeen");
            TestSeverity = InfoBarSeverity.Success;
            Fingerprint = UiText.Get("ServerDetailViewModelConnection_VerifiedHostKey", result.HostKey.Algorithm, result.HostKey.Fingerprint);
        }
        catch (OperationCanceledException)
        {
            if (!_active) { return; }
            TestTitle = UiText.Get("ServerDetailViewModelConnection_ConnectionTestCancelled");
            TestMessage = UiText.Get("ServerDetailViewModelConnection_YouCanEditTheFormOrTryAgain");
        }
        catch (ConnectionException exception)
        {
            if (!_active) { return; }
            TestTitle = UiText.Get("ServerDetailViewModelConnection_ConnectionFailed");
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
            TestTitle = UiText.Get("ServerDetailViewModelConnection_ConnectionFailed");
            TestMessage = UiText.Get("ServerDetailViewModelConnection_TheConnectionTestCouldNotFinishCheckThe");
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
