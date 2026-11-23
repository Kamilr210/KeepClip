using System.Windows;
using KeepClip.Localization;

namespace KeepClip.UI;

public partial class OnboardingWindow : Window
{
    private readonly (string TitleKey, string BodyKey)[] _steps =
    {
        ("onboarding.steps.welcome.title", "onboarding.steps.welcome.body"),
        ("onboarding.steps.automatic-clips.title", "onboarding.steps.automatic-clips.body"),
        ("onboarding.steps.clips-folder.title", "onboarding.steps.clips-folder.body"),
        ("onboarding.steps.games-organized.title", "onboarding.steps.games-organized.body"),
        ("onboarding.steps.clip-editing.title", "onboarding.steps.clip-editing.body"),
        ("onboarding.steps.transcription.title", "onboarding.steps.transcription.body"),
        ("onboarding.steps.transcript-edit.title", "onboarding.steps.transcript-edit.body"),
    };

    private int _index;

    public OnboardingWindow()
    {
        InitializeComponent();
        RenderStep();
    }

    private void RenderStep()
    {
        var (titleKey, bodyKey) = _steps[_index];
        StepBadge.Text = Loc.Format("onboarding.stepOf", ("n", _index + 1), ("total", _steps.Length));
        StepTitle.Text = Loc.T(titleKey);
        StepBody.Text = Loc.T(bodyKey);
        PrevBtn.Visibility = _index > 0 ? Visibility.Visible : Visibility.Collapsed;
        NextBtn.Content = _index == _steps.Length - 1 ? Loc.T("onboarding.finish") : Loc.T("onboarding.next");
    }

    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        if (_index > 0) { _index--; RenderStep(); }
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_index < _steps.Length - 1)
        {
            _index++;
            RenderStep();
        }
        else
        {
            OnboardingService.Patch(started: true, completed: true, skipped: false, currentStep: null, seenHint: null);
            Close();
        }
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        OnboardingService.Patch(started: true, completed: null, skipped: true, currentStep: null, seenHint: null);
        Close();
    }
}
