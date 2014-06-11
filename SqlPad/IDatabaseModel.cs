﻿using System.Collections.Generic;
using System.Configuration;

namespace SqlPad
{
	public interface IDatabaseModel
	{
		ConnectionStringSettings ConnectionString { get; }

		string CurrentSchema { get; }

		ICollection<string> Schemas { get; }

		void Refresh();
	}

	public interface IDatabaseObject
	{
		string Name { get; }

		string Type { get; }

		string Owner { get; }
	}

	public interface IColumn
	{
		string Name { get; }

		string FullTypeName { get; }
	}
}
