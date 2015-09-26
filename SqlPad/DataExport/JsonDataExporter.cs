using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SqlPad.DataExport
{
	public class JsonDataExporter : IDataExporter
	{
		private const string MaskJsonValue = "    \"{0}\": {{{1}}}";
		private const string QuoteCharacter = "\"";
		private const string EscapedQuote = "\\\"";

		public string FileNameFilter => "JSON files (*.json)|*.json|All files (*.*)|*";

		public bool HasAppendSupport { get; } = false;

		public void ExportToClipboard(ResultViewer resultViewer, IDataExportConverter dataExportConverter)
		{
			ExportToFile(null, resultViewer, dataExportConverter);
		}

		public void ExportToFile(string fileName, ResultViewer resultViewer, IDataExportConverter dataExportConverter)
		{
			ExportToFileAsync(fileName, resultViewer, dataExportConverter, CancellationToken.None).Wait();
		}

		public Task ExportToClipboardAsync(ResultViewer resultViewer, IDataExportConverter dataExportConverter, CancellationToken cancellationToken)
		{
			return ExportToFileAsync(null, resultViewer, dataExportConverter, cancellationToken);
		}

		public Task ExportToFileAsync(string fileName, ResultViewer resultViewer, IDataExportConverter dataExportConverter, CancellationToken cancellationToken)
		{
			var orderedColumns = DataExportHelper.GetOrderedExportableColumns(resultViewer.ResultGrid);
			var columnHeaders = orderedColumns
				.Select((h, i) => String.Format(MaskJsonValue, h.Name.Replace("{", "{{").Replace("}", "}}").Replace(QuoteCharacter, EscapedQuote), i));

			var jsonTemplateBuilder = new StringBuilder();
			jsonTemplateBuilder.AppendLine("  {{");
			jsonTemplateBuilder.AppendLine(String.Join($",{Environment.NewLine}", columnHeaders));
			jsonTemplateBuilder.Append("  }}");

			var rows = resultViewer.ResultGrid.Items;

			return DataExportHelper.RunExportActionAsync(fileName, w => ExportInternal(orderedColumns, jsonTemplateBuilder.ToString(), rows, w, dataExportConverter, cancellationToken));
		}

		private void ExportInternal(IReadOnlyList<ColumnHeader> orderedColumns, string jsonTemplate, ICollection rows, TextWriter writer, IDataExportConverter dataExportConverter, CancellationToken cancellationToken)
		{
			writer.WriteLine('[');

			var rowIndex = 1;
			foreach (object[] rowValues in rows)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var values = orderedColumns.Select(h => (object)FormatJsonValue(rowValues[h.ColumnIndex], dataExportConverter)).ToArray();
				writer.Write(jsonTemplate, values);

				if (rowIndex < rows.Count)
				{
					writer.WriteLine(',');
				}

				rowIndex++;
			}

			writer.WriteLine();
			writer.Write(']');
		}

		private static string FormatJsonValue(object value, IDataExportConverter dataExportConverter)
		{
			return DataExportHelper.IsNull(value) ? "null" : dataExportConverter.ToJson(value);
		}
	}
}
