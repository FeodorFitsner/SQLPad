using System.Collections.Generic;

namespace SqlPad.Oracle
{
	public abstract class OracleReference
	{
		protected OracleReference()
		{
			ObjectNodeObjectReferences = new HashSet<OracleObjectWithColumnsReference>();
		}

		public virtual OracleObjectIdentifier FullyQualifiedObjectName
		{
			get { return OracleObjectIdentifier.Create(OwnerNode, ObjectNode, null); }
		}

		public abstract string Name { get; }

		public string NormalizedName { get { return Name.ToQuotedIdentifier(); } }

		public OracleQueryBlock Owner { get; set; }

		public StatementGrammarNode RootNode { get; set; }

		public StatementGrammarNode OwnerNode { get; set; }

		public StatementGrammarNode ObjectNode { get; set; }

		public ICollection<OracleObjectWithColumnsReference> ObjectNodeObjectReferences { get; set; }

		public OracleSchemaObject SchemaObject { get; set; }

		public OracleSelectListColumn SelectListColumn { get; set; }

		public OracleReferenceContainer Container
		{
			get { return SelectListColumn ?? (OracleReferenceContainer)Owner; }
		}

		public StatementGrammarNode DatabaseLinkNode { get; set; }

		public OracleDatabaseLink DatabaseLink { get; set; }
	}
}
