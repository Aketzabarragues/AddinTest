using System;
using System.Linq;
using System.Windows.Forms;
using Siemens.Engineering;
using Siemens.Engineering.AddIn.Menu;
using Siemens.Engineering.CrossReference;
using Siemens.Engineering.Hmi.Tag;
using Siemens.Engineering.SW.Blocks;

namespace Siemens_DB_Tool
{
    public class AddIn : ContextMenuAddIn
    {
        private const string s_DisplayNameOfAddIn = "Herramientas ABH";
        private GlobalDB _db;
        public AddIn(TiaPortal tiaportal) : base(s_DisplayNameOfAddIn) { }

        protected override void BuildContextMenuItems(ContextMenuAddInRoot addInRootSubmenu)
        {
            // BOTÓN 1: RÁPIDO (Solo mira si se usa o no)
            addInRootSubmenu.Items.AddActionItem<IEngineeringObject>(
                "Auditar DB (Rápido - En Uso/Libre)",
                OnAnalyzeFastClick,
                OnCheckIfGlobalDb);

            // BOTÓN 2: DETALLADO (Cuenta líneas de código, más lento)
            addInRootSubmenu.Items.AddActionItem<IEngineeringObject>(
                "Auditar DB (Detallado - Conteo total)",
                OnAnalyzeDetailedClick,
                OnCheckIfGlobalDb);

            // BOTÓN 3: MANUAL (PREAL - Asíncrono)
            addInRootSubmenu.Items.AddActionItem<IEngineeringObject>(
                "Auditar DB PREAL (Usar solo con DB parametros reales)",
                OnAnalyzePRealClick,
                OnCheckIfGlobalDb);
        }

        private MenuStatus OnCheckIfGlobalDb(MenuSelectionProvider<IEngineeringObject> selectionProvider)
        {
            var selection = selectionProvider.GetSelection();
            if (selection.Count() == 1 && selection.First() is GlobalDB) return MenuStatus.Enabled;
            return MenuStatus.Hidden;
        }

        // --- Manejador para modo RÁPIDO ---
        private void OnAnalyzeFastClick(MenuSelectionProvider<IEngineeringObject> selectionProvider)
        {
            LaunchAudit(selectionProvider, modoRapido: true);
        }

        // --- Manejador para modo DETALLADO ---
        private void OnAnalyzeDetailedClick(MenuSelectionProvider<IEngineeringObject> selectionProvider)
        {
            LaunchAudit(selectionProvider, modoRapido: false);
        }

        // --- Manejador para modo PREAL ---
        private void OnAnalyzePRealClick(MenuSelectionProvider<IEngineeringObject> selectionProvider)
        {
            LaunchPRealAudit(selectionProvider);
        }

        private void LaunchAudit(MenuSelectionProvider<IEngineeringObject> provider, bool modoRapido)
        {
            GlobalDB db = provider.GetSelection().First() as GlobalDB;
            if (db == null) return;

            // Instancia del auditor general (El primero que hicimos)
            ProgressDialog dialog = new ProgressDialog(db, modoRapido);
            dialog.ShowDialog();
        }

        private void LaunchPRealAudit(MenuSelectionProvider<IEngineeringObject> provider)
        {
            GlobalDB db = provider.GetSelection().First() as GlobalDB;
            if (db == null) return;

            // --- CORRECCIÓN AQUÍ ---
            // Instanciamos la NUEVA clase asíncrona que evita el bloqueo "No Responde"
            ProgressDialog_ZC_Array_Async dialog = new ProgressDialog_ZC_Array_Async(db);

            // ShowDialog() mostrará la ventana y el evento Shown dentro de la clase 
            // disparará el hilo en segundo plano automáticamente.
            dialog.ShowDialog();
        }
    }
}