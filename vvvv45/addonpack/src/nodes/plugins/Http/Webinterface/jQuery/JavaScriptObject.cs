using System;
using System.Collections.Generic;
using System.Text;

namespace VVVV.Nodes.jQuery
{
	public abstract class JavaScriptObject : IScriptGenerator
	{

		#region IScriptGenerator Members

		public abstract string PScript
		{
			get;
		}

		#endregion
	}
}