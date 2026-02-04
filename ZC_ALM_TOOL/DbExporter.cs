using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Windows.Forms;
using Siemens.Engineering;
using Siemens.Engineering.SW.Blocks; // Para GlobalDB

namespace Siemens_DB_Tool
{
    /// <summary>
    /// Esta clase se encarga EXCLUSIVAMENTE de la lógica de exportación.
    /// No sabe nada de menús ni de botones.
    /// </summary>
    public class DbExporter
    {
        public void ExportarACsv(GlobalDB db)
        {
            // 1. Definir rutas
            string tempXmlPath = Path.Combine(Path.GetTempPath(), db.Name + ".xml");
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string csvPath = Path.Combine(desktopPath, db.Name + "_Variables.csv");

            try
            {
                // Exportar
                db.Export(new FileInfo(tempXmlPath), ExportOptions.WithDefaults);

                // Cargar XML
                XDocument doc = XDocument.Load(tempXmlPath);

                // Escribir CSV
                using (StreamWriter sw = new StreamWriter(csvPath, false, System.Text.Encoding.UTF8))
                {
                    sw.WriteLine("RutaCompleta;Nombre;TipoDatos;Comentario;Valor");

                    var variables = doc.Descendants().Where(x => x.Name.LocalName == "Member");
                    int contador = 0;

                    foreach (var variable in variables)
                    {
                        string ruta = ObtenerRutaCompleta(variable);
                        string nombre = variable.Attribute("Name")?.Value ?? "";
                        string tipo = variable.Attribute("Datatype")?.Value;
                        if (string.IsNullOrEmpty(tipo)) tipo = "Struct/UDT";

                        // Comentario
                        string comentario = "";
                        var commentNode = variable.Elements().FirstOrDefault(e => e.Name.LocalName == "Comment");
                        if (commentNode != null)
                        {
                            var texto = commentNode.Descendants().FirstOrDefault(e => e.Name.LocalName == "Text");
                            if (texto != null)
                                comentario = texto.Value.Replace(";", ",").Replace("\r", "").Replace("\n", " ");
                        }

                        // Valor
                        string valor = "";
                        var startValNode = variable.Elements().FirstOrDefault(e => e.Name.LocalName == "StartValue");
                        if (startValNode != null)
                            valor = startValNode.Value.Replace(";", ",").Replace("\r", "").Replace("\n", "");

                        if (!string.IsNullOrEmpty(nombre))
                        {
                            sw.WriteLine($"{ruta};{nombre};{tipo};{comentario};{valor}");
                            contador++;
                        }
                    }

                    MessageBox.Show($"Exportación finalizada.\nVariables: {contador}\nRuta: {csvPath}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error en la exportación: " + ex.Message);
            }
            finally
            {
                if (File.Exists(tempXmlPath)) File.Delete(tempXmlPath);
            }
        }

        // Método auxiliar privado (solo lo usa esta clase)
        private string ObtenerRutaCompleta(XElement elementoActual)
        {
            string nombre = elementoActual.Attribute("Name")?.Value ?? "";
            XElement padre = elementoActual.Parent;

            while (padre != null && padre.Name.LocalName != "Interface")
            {
                if (padre.Name.LocalName == "Member")
                {
                    string nombrePadre = padre.Attribute("Name")?.Value ?? "";
                    if (!string.IsNullOrEmpty(nombrePadre))
                    {
                        nombre = nombrePadre + "." + nombre;
                    }
                }
                padre = padre.Parent;
            }
            return nombre;
        }
    }
}