using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Siemens.Engineering.CrossReference;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;

namespace Siemens_DB_Tool
{
    public class ProgressDialog : Form
    {
        private ProgressBar _progressBar;
        private Label _lblStatus;
        private Button _btnCancel;
        private bool _cancelRequested = false;

        private GlobalDB _db;
        private bool _modoRapido; // <--- NUEVA VARIABLE PARA SABER QUÉ HACER

        private StringBuilder _logBuilder;
        private int _itemsProcessed = 0;

        // Constructor modificado para recibir el modo
        public ProgressDialog(GlobalDB db, bool modoRapido)
        {
            _db = db;
            _modoRapido = modoRapido;
            InitializeComponent();
            _logBuilder = new StringBuilder();
        }

        private void InitializeComponent()
        {
            this.Size = new Size(550, 220);
            this.Text = _modoRapido ? "Auditoría Rápida (En Uso/Libre)" : "Auditoría Detallada (Contando usos...)";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ControlBox = false;
            this.TopMost = true;

            _lblStatus = new Label() { Left = 20, Top = 20, Width = 500, Text = "Preparando..." };
            _progressBar = new ProgressBar() { Left = 20, Top = 50, Width = 500, Height = 30, Style = ProgressBarStyle.Continuous };

            _btnCancel = new Button() { Left = 220, Top = 100, Width = 100, Text = "Cancelar", BackColor = Color.IndianRed, ForeColor = Color.White };
            _btnCancel.Click += (s, e) => {
                _cancelRequested = true;
                _lblStatus.Text = "Cancelando... terminando variable actual...";
                _btnCancel.Enabled = false;
            };

            this.Controls.Add(_lblStatus);
            this.Controls.Add(_progressBar);
            this.Controls.Add(_btnCancel);

            this.Shown += StartProcessing;
        }







        private async void StartProcessing(object sender, EventArgs e)
        {
            Application.DoEvents();
            await Task.Delay(100);

            string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"Reporte_{_db.Name}.csv");
            _logBuilder.Clear(); // Limpiamos por si acaso

            try
            {
                UpdateStatus("Fase 1: Consultando TIA Portal...");

                // ---------------------------------------------------------
                // CRONÓMETRO 1: TIEMPO DE CONSULTA API (NETO)
                // ---------------------------------------------------------
                Stopwatch swQuery = Stopwatch.StartNew();

                var xRefService = _db.GetService<CrossReferenceService>();
                // Esta línea trae los punteros iniciales. Suele ser rápida.
                var rootResult = xRefService.GetCrossReferences(CrossReferenceFilter.UnusedObjects);

                swQuery.Stop();
                // ---------------------------------------------------------

                // Escribimos la cabecera del CSV UNA SOLA VEZ aquí fuera
                _logBuilder.AppendLine("Nombre de Variable;Dirección;Tipo de Dato;Estado;Usos");

                UpdateStatus("Fase 2: Iterando y procesando datos...");

                _progressBar.Maximum = rootResult.Sources.Count + 100;
                _progressBar.Value = 0;
                _itemsProcessed = 0;

                // ---------------------------------------------------------
                // CRONÓMETRO 2: TIEMPO DE ITERACIÓN (CUELLO DE BOTELLA)
                // ---------------------------------------------------------
                Stopwatch swIteracion = Stopwatch.StartNew();

                // Llamada al método recursivo
                ProcessSourceObjects(rootResult.Sources);

                swIteracion.Stop();
                // ---------------------------------------------------------

                if (!_cancelRequested)
                {
                    // Añadimos las estadísticas al final del CSV o en un archivo aparte si prefieres
                    // Lo pondré al final como comentarios para no romper la estructura de columnas
                    _logBuilder.AppendLine("");
                    _logBuilder.AppendLine($";;;ESTADÍSTICAS;");
                    _logBuilder.AppendLine($";;;Tiempo Consulta API (Indexación);{swQuery.Elapsed.TotalSeconds:F3} s");
                    _logBuilder.AppendLine($";;;Tiempo Iteración C# (Loop);{swIteracion.Elapsed.TotalSeconds:F3} s");
                    _logBuilder.AppendLine($";;;Total Elementos;{_itemsProcessed}");

                    File.WriteAllText(logPath, _logBuilder.ToString(), Encoding.UTF8);

                    string mensaje = $"Proceso Terminado.\n\n" +
                                     $"Consulta TIA: {swQuery.Elapsed.TotalSeconds:F3} s\n" +
                                     $"Iteración (Loop): {swIteracion.Elapsed.TotalSeconds:F3} s\n\n" +
                                     $"Archivo guardado en Escritorio.";

                    MessageBox.Show(mensaje, "Métricas de Rendimiento");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error Crítico");
            }
            finally
            {
                this.Close();
            }
        }

       


        private void ProcessSourceObjects(SourceObjectComposition sourceObjects)
        {
            // Convertimos a lista. Si el filtro funcionó, esto tendrá pocos elementos.
            var lista = sourceObjects.Cast<SourceObject>().ToList();

            foreach (var source in lista)
            {
                if (_cancelRequested) return;

                // YA NO NECESITAMOS COMPROBAR SI SE USA O NO.
                // Si está en esta lista, es porque TIA Portal dice que es "UnusedObject".

                string nombre = source.Name;     // [cite: 48]
                string direccion = source.Address; // [cite: 48]
                string tipo = source.TypeName;   // [cite: 48]

                // Escribimos directamente
                _logBuilder.AppendLine($"{nombre};{direccion};{tipo};NO USADA");

                // Actualización UI
                _itemsProcessed++;
                UpdateStatus($"Encontrada basura: {nombre}...");
                Application.DoEvents();

                // Recursividad: Vital para encontrar miembros de Structs no usados
                try
                {
                    if (source.Children != null && source.Children.Count > 0)
                    {
                        ProcessSourceObjects(source.Children);
                    }
                }
                catch { }
            }
        }








        private void UpdateStatus(string text)
        {
            _lblStatus.Text = text;
            Application.DoEvents();
        }
    }
}