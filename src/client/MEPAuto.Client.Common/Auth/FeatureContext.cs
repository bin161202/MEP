using Autodesk.Revit.UI;
using MEPAuto.Client.Common.Revit;

namespace MEPAuto.Client.Common.Auth
{
    public class FeatureContext : IFeatureContext
    {
        public IRevitService RevitSvc { get; }
        public IServerProxy Server { get; }
        public CurrentUserInfo CurrentUser { get; }
        public UIApplication UiApp { get; }

        public FeatureContext(IRevitService revitSvc, IServerProxy server, CurrentUserInfo user, UIApplication uiApp)
        {
            RevitSvc = revitSvc;
            Server = server;
            CurrentUser = user;
            UiApp = uiApp;
        }
    }
}
