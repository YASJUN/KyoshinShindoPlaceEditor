using System;
using System.Windows.Forms;

namespace KyoshinShindoPlaceEditor
{
	internal static class Program
	{
		/// <summary>
		/// アプリケーションのメイン エントリ ポイントです。
		/// </summary>
		[STAThread]
		private static void Main(string[] args)
		{
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			if (Array.IndexOf(args, "--legacy") >= 0)
				Application.Run(new MainForm());
			else
				Application.Run(new ModernEditorForm());
		}
	}
}
