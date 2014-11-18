using System.Linq;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Folding;

namespace SqlPad
{
	public class SqlFoldingStrategy
	{
		private readonly FoldingManager _foldingManager;
		private readonly TextEditor _editor;

		public SqlFoldingStrategy(FoldingManager foldingManager, TextEditor editor)
		{
			_foldingManager = foldingManager;
			_editor = editor;
		}

		public void UpdateFoldings(StatementCollection statements)
		{
			var foldings = statements.SelectMany(s => s.Sections)
				.Where(IsMultilineOrNestedSection)
				.Select(s => new NewFolding(s.FoldingStart, s.FoldingEnd) {Name = s.Placeholder});
			
			_foldingManager.UpdateFoldings(foldings, -1);
		}

		public void Store(WorkDocument workDocument)
		{
			workDocument.UpdateFoldingStates(_foldingManager.AllFoldings.Select(f => f.IsFolded));
		}

		public void Restore(WorkDocument workDocument)
		{
			var foldingEnumerator = _foldingManager.AllFoldings.GetEnumerator();
			foreach (var isFolded in workDocument.FoldingStates.Where(s => foldingEnumerator.MoveNext()))
			{
				foldingEnumerator.Current.IsFolded = isFolded;
			}
		}

		private bool IsMultilineOrNestedSection(FoldingSection section)
		{
			return section.IsNestedSection || _editor.GetLineNumberByOffset(section.FoldingStart) != _editor.GetLineNumberByOffset(section.FoldingEnd);
		}
	}
}
