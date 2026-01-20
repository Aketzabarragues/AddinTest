using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks; // Necesario para Task
using System.Windows.Forms;
using Siemens.Engineering.CrossReference;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;

namespace Siemens_DB_Tool
{
    public class ProgressDialog_ZC_Array_Async : Form
    {
        private ProgressBar _progressBar;
        private Label _lblStatus;
        private Button _btnCancel;

        // Volatile asegura que el hilo secundario lea el valor actualizado al instante
        private volatile bool _cancelRequested = false;

        private GlobalDB _db;
        private StringBuilder _logBuilder;
        private int _itemsProcessed = 0;

        public ProgressDialog_ZC_Array_Async(GlobalDB db)
        {
            _db = db;
            InitializeComponent();
            _logBuilder = new StringBuilder();
        }

        private void InitializeComponent()
        {
            this.Size = new Size(550, 220);
            this.Text = "Auditoría Array (Asíncrona - No Bloqueante)";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ControlBox = false;
            this.TopMost = true;

            _lblStatus = new Label() { Left = 20, Top = 20, Width = 500, Text = "Iniciando motor asíncrono..." };
            _progressBar = new ProgressBar() { Left = 20, Top = 50, Width = 500, Height = 30, Style = ProgressBarStyle.Continuous };

            _btnCancel = new Button() { Left = 220, Top = 100, Width = 100, Text = "Cancelar", BackColor = Color.IndianRed, ForeColor = Color.White };
            _btnCancel.Click += (s, e) => {
                _cancelRequested = true;
                _lblStatus.Text = "Cancelando... Espere a que termine la tarea actual.";
                _btnCancel.Enabled = false;
            };

            this.Controls.Add(_lblStatus);
            this.Controls.Add(_progressBar);
            this.Controls.Add(_btnCancel);

            this.Shown += async (s, e) => await StartProcessingAsync();
        }

        // Método principal marcado como ASYNC
        private async Task StartProcessingAsync()
        {
            // Ruta del archivo
            string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"Reporte_Array_{_db.Name}.csv");
            _logBuilder.Clear();

            // Lanzamos la tarea pesada a un hilo secundario para liberar la UI
            await Task.Run(() =>
            {
                try
                {
                    SafeUpdateStatus("Consultando TIA Portal (Operación pesada)...", 0);

                    // 1. Obtener servicio y datos (Esto puede tardar, pero ya no congela la ventana)
                    var xRefService = _db.GetService<CrossReferenceService>();

                    // Nota: Acceder a TIA desde un hilo secundario suele funcionar en Add-Ins.
                    // Si da error de Threading, habría que encapsular esto, pero probemos así primero.
                    var rootResult = xRefService.GetCrossReferences(CrossReferenceFilter.AllObjects);

                    _logBuilder.AppendLine("Indice Array;Estado;Nombre Variable;Usos Directos");

                    // Forzamos la lista en memoria para trabajar
                    var listaRaiz = rootResult.Sources.Cast<SourceObject>().ToList();

                    int total = listaRaiz.Count;
                    SafeUpdateMaxProgress(total + 100);

                    // 2. Iniciar lógica recursiva
                    ProcessSpecificArray(listaRaiz);

                    // 3. Guardado (Solo si no se canceló)
                    if (!_cancelRequested)
                    {
                        File.WriteAllText(logPath, _logBuilder.ToString(), Encoding.UTF8);

                        // Volvemos al hilo de UI para mostrar el mensaje final
                        this.Invoke((MethodInvoker)delegate
                        {
                            MessageBox.Show($"Proceso finalizado.\nReporte guardado en Escritorio.", "Éxito");
                            this.Close();
                        });
                    }
                }
                catch (Exception ex)
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        MessageBox.Show($"Error en el hilo de proceso: {ex.Message}", "Error");
                        this.Close();
                    });
                }
            });

            // Si se canceló, cerramos
            if (_cancelRequested) this.Close();
        }

        private void ProcessSpecificArray(List<SourceObject> sources)
        {
            foreach (var source in sources)
            {
                if (_cancelRequested) return;

                // Actualizamos la UI cada X elementos
                _itemsProcessed++;
                if (_itemsProcessed % 20 == 0)
                {
                    SafeUpdateStatus($"Analizando: {source.Name}...", _itemsProcessed);
                }

                bool tieneHijos = false;
                List<SourceObject> hijos = null;

                try
                {
                    // Intentamos leer hijos. Esto es lo que solía bloquear.
                    if (source.Children != null && source.Children.Count > 0)
                    {
                        tieneHijos = true;
                        hijos = source.Children.Cast<SourceObject>().ToList();
                    }
                }
                catch { /* Ignorar objetos que no soportan hijos */ }

                if (tieneHijos && hijos != null)
                {
                    var primerHijo = hijos.FirstOrDefault();
                    // Lógica de detección: Si el hijo se llama "Algo[0]", el padre es el Array
                    if (primerHijo != null && primerHijo.Name.Contains("["))
                    {
                        ProcesarIndicesDelArray(hijos);
                    }
                    else
                    {
                        // Sigue siendo una carpeta o struct, profundizamos
                        ProcessSpecificArray(hijos);
                    }
                }
            }
        }

        private void ProcesarIndicesDelArray(List<SourceObject> indices)
        {
            foreach (var indiceObj in indices)
            {
                if (_cancelRequested) return;

                bool enUso = false;
                int count = 0;

                try
                {
                    // Consulta API rápida
                    var refs = indiceObj.References;
                    if (refs != null && refs.Count > 0)
                    {
                        enUso = true;
                        // Opcional: contar
                        foreach (var r in refs) count += r.Locations.Count;
                    }
                }
                catch { }

                string nombreCompleto = indiceObj.Name;
                string numero = ExtraerNumero(nombreCompleto);
                string estado = enUso ? "EN USO" : "LIBRE";

                // Escribir en StringBuilder (es seguro escribir en StringBuilder desde hilos si no hay concurrencia, aquí es secuencial)
                _logBuilder.AppendLine($"{numero};{estado};{nombreCompleto};{count}");
            }
        }

        private string ExtraerNumero(string nombre)
        {
            try
            {
                int start = nombre.LastIndexOf('[');
                int end = nombre.LastIndexOf(']');
                if (start != -1 && end != -1 && end > start)
                    return nombre.Substring(start + 1, end - start - 1);
                return nombre;
            }
            catch { return "Err"; }
        }

        // --- MÉTODOS SEGUROS PARA LA UI (THREAD-SAFE) ---

        private void SafeUpdateStatus(string text, int value)
        {
            // Preguntamos si necesitamos invocar (estamos en otro hilo?)
            if (_lblStatus.InvokeRequired)
            {
                _lblStatus.BeginInvoke((MethodInvoker)delegate
                {
                    _lblStatus.Text = text;
                    if (value > 0 && value < _progressBar.Maximum) _progressBar.Value = value;
                });
            }
            else
            {
                _lblStatus.Text = text;
                if (value > 0 && value < _progressBar.Maximum) _progressBar.Value = value;
            }
        }

        private void SafeUpdateMaxProgress(int max)
        {
            if (_progressBar.InvokeRequired)
            {
                _progressBar.BeginInvoke((MethodInvoker)delegate { _progressBar.Maximum = max; });
            }
            else
            {
                _progressBar.Maximum = max;
            }
        }
    }
}