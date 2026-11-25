using System;
using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using VelecCableRouter.Core;
using VelecCableRouter.Core.Reporting;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace VelecCableRouter.AutoCAD
{
    /// <summary>Commands VELEC_BUILD_CABLES and VELEC_CLEAR_DEBUG (issue #1, section 5).</summary>
    public sealed class CableCommands
    {
        // Options are remembered for the AutoCAD session, like the options of standard commands.
        private static double epsilon = CableBuildOptions.DefaultEpsilon;
        private static bool diagnostics;
        private static bool useSuperLineLayer;

        [CommandMethod("VELEC_BUILD_CABLES", CommandFlags.Modal | CommandFlags.UsePickSet | CommandFlags.Redraw)]
        public static void BuildCables()
        {
            var document = AcApplication.DocumentManager.MdiActiveDocument;
            if (document == null)
            {
                return;
            }

            var editor = document.Editor;
            try
            {
                var selection = Select(editor);
                if (selection == null || !AskOptions(editor))
                {
                    return;
                }

                Build(document, selection);
            }
            catch (System.Exception error)
            {
                editor.WriteMessage("\nVELEC_BUILD_CABLES: ошибка — {0}\n", error.Message);
            }
        }

        [CommandMethod("VELEC_CLEAR_DEBUG", CommandFlags.Modal)]
        public static void ClearDebug()
        {
            var document = AcApplication.DocumentManager.MdiActiveDocument;
            if (document == null)
            {
                return;
            }

            try
            {
                int erased;
                using (var transaction = document.Database.TransactionManager.StartTransaction())
                {
                    erased = DrawingWriter.EraseFromLayouts(document.Database, transaction, DebugLayers.IsDebugLayer);
                    transaction.Commit();
                }

                document.Editor.WriteMessage("\nVELEC_CLEAR_DEBUG: удалено диагностических примитивов: {0}.\n", erased);
            }
            catch (System.Exception error)
            {
                document.Editor.WriteMessage("\nVELEC_CLEAR_DEBUG: ошибка — {0}\n", error.Message);
            }
        }

        private static SelectionSet? Select(Editor editor)
        {
            var implied = editor.SelectImplied();
            if (implied.Status == PromptStatus.OK && implied.Value != null && implied.Value.Count > 0)
            {
                editor.SetImpliedSelection(Array.Empty<ObjectId>());
                return implied.Value;
            }

            var options = new PromptSelectionOptions
            {
                MessageForAdding = "\nВыберите трассы (SuperLine) и устройства (Device)",
            };
            var result = editor.GetSelection(options);
            return result.Status == PromptStatus.OK ? result.Value : null;
        }

        /// <summary>Option loop: Enter builds the cables, Esc cancels the command.</summary>
        private static bool AskOptions(Editor editor)
        {
            while (true)
            {
                var prompt = new PromptKeywordOptions(string.Format(
                    CultureInfo.InvariantCulture,
                    "\nДопуск = {0}, Диагностика = {1}, Слой кабелей = {2}. Параметры",
                    epsilon,
                    diagnostics ? "Да" : "Нет",
                    useSuperLineLayer ? "слой трассы" : "текущий"))
                {
                    AllowNone = true,
                };
                prompt.Keywords.Add("Tolerance", "ДОпуск", "ДОпуск");
                prompt.Keywords.Add("Diagnostics", "ДИагностика", "ДИагностика");
                prompt.Keywords.Add("Layer", "Слой", "Слой");
                prompt.Keywords.Add("Build", "Построить", "Построить");
                prompt.Keywords.Default = "Build";
                var answer = editor.GetKeywords(prompt);
                if (answer.Status == PromptStatus.None)
                {
                    return true;
                }

                if (answer.Status != PromptStatus.OK)
                {
                    return false;
                }

                switch (answer.StringResult)
                {
                    case "Build":
                        return true;
                    case "Tolerance":
                        var value = editor.GetDouble(new PromptDoubleOptions("\nДопуск (Epsilon)")
                        {
                            AllowNegative = false,
                            AllowZero = false,
                            AllowNone = true,
                            DefaultValue = epsilon,
                            UseDefaultValue = true,
                        });
                        if (value.Status == PromptStatus.OK && CableBuildOptions.IsValidEpsilon(value.Value))
                        {
                            epsilon = value.Value;
                        }
                        else if (value.Status == PromptStatus.Cancel)
                        {
                            return false;
                        }

                        break;
                    case "Diagnostics":
                        var yesNo = Choose(editor, "\nРисовать граф на слоях VELEC_DEBUG_*?", "Yes", "Да", "No", "Нет", diagnostics);
                        if (yesNo == null)
                        {
                            return false;
                        }

                        diagnostics = yesNo.Value;
                        break;
                    case "Layer":
                        var layer = Choose(editor, "\nСлой кабелей", "Route", "Трассы", "Current", "Текущий", useSuperLineLayer);
                        if (layer == null)
                        {
                            return false;
                        }

                        useSuperLineLayer = layer.Value;
                        break;
                }
            }
        }

        private static bool? Choose(Editor editor, string message, string trueGlobal, string trueLocal, string falseGlobal, string falseLocal, bool current)
        {
            var prompt = new PromptKeywordOptions(message) { AllowNone = true };
            prompt.Keywords.Add(trueGlobal, trueLocal, trueLocal);
            prompt.Keywords.Add(falseGlobal, falseLocal, falseLocal);
            prompt.Keywords.Default = current ? trueGlobal : falseGlobal;
            var answer = editor.GetKeywords(prompt);
            if (answer.Status == PromptStatus.None)
            {
                return current;
            }

            return answer.Status == PromptStatus.OK ? answer.StringResult == trueGlobal : (bool?)null;
        }

        private static void Build(Document document, SelectionSet selection)
        {
            var database = document.Database;
            var editor = document.Editor;
            var options = new CableBuildOptions { Epsilon = epsilon };
            var created = 0;
            var debugEntities = 0;
            CableBuildResult result;
            using (var transaction = database.TransactionManager.StartTransaction())
            {
                var entities = AutoCadEntityReader.Read(transaction, AutoCadEntityReader.Ids(selection));
                result = CableBuildService.Build(entities, options);
                var writer = new DrawingWriter(database, transaction);
                writer.EnsureRegApp();
                foreach (var cable in result.Cables)
                {
                    writer.AddCable(cable, useSuperLineLayer);
                    created++;
                }

                if (diagnostics)
                {
                    foreach (var report in result.SuperLines)
                    {
                        debugEntities += writer.DrawDebug(DebugGeometryPlanner.Plan(report, options.Epsilon));
                    }
                }

                transaction.Commit();
            }

            editor.WriteMessage("\nVELEC_BUILD_CABLES (допуск {0}):\n", epsilon.ToString(CultureInfo.InvariantCulture));
            foreach (var line in ReportFormatter.Format(result, created))
            {
                editor.WriteMessage(line + "\n");
            }

            if (diagnostics)
            {
                editor.WriteMessage("Диагностика: {0} примитивов на слоях {1}*. Удаление: VELEC_CLEAR_DEBUG.\n", debugEntities, DebugLayers.Prefix);
            }
        }
    }
}
