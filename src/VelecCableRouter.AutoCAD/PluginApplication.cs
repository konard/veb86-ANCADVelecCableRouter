using Autodesk.AutoCAD.Runtime;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: ExtensionApplication(typeof(VelecCableRouter.AutoCAD.PluginApplication))]
[assembly: CommandClass(typeof(VelecCableRouter.AutoCAD.CableCommands))]

namespace VelecCableRouter.AutoCAD
{
    /// <summary>Entry point loaded by AutoCAD (NETLOAD or an autoloader bundle).</summary>
    public sealed class PluginApplication : IExtensionApplication
    {
        public void Initialize()
        {
            // No document may be open while AutoCAD starts; the message is optional.
            var document = AcApplication.DocumentManager.MdiActiveDocument;
            document?.Editor.WriteMessage(
                "\nVelecCableRouter загружен. Команды: VELEC_BUILD_CABLES — построить кабели, VELEC_CLEAR_DEBUG — удалить диагностику.\n");
        }

        public void Terminate()
        {
        }
    }
}
