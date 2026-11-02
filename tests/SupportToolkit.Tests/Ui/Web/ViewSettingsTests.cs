using SupportToolkit.Ui.Web;

namespace SupportToolkit.Tests.Ui.Web;

public class ViewSettingsTests
{
    [Fact]
    public void TheDefault_IsTheSimpleView()
    {
        Assert.False(new ViewSettings().IsDetailed);
    }

    [Fact]
    public void ChangingIt_TellsThePages_ButSettingTheSameValueDoesNot()
    {
        var view = new ViewSettings();
        var changes = 0;

        view.Changed += () => changes++;

        view.Set(true);
        view.Set(true);
        view.Set(false);

        Assert.Equal(2, changes);
        Assert.False(view.IsDetailed);
    }

    [Fact]
    public void TheWebState_StartsSimple()
    {
        var (service, _, _) =
            StatusTestSupport.Create(StatusTestSupport.FullConfiguration());

        Assert.False(new WebAppState(service).View.IsDetailed);
    }
}
