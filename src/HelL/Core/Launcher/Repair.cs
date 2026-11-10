namespace HelL.Core.Launcher;

public static class Repair
{
	public static string RepairVersion(Installer installer, object version_id0)
	{
		string text = Py.Strip(Py.S(version_id0));
		if (text.Length == 0)
		{
			throw new InstallError("请选择要修复的版本");
		}
		PyDict pyDict = installer.instance.VersionJson(text) as PyDict;
		if (!Py.Truthy(pyDict))
		{
			throw new InstallError("版本 " + text + " 未安装");
		}
		installer._note("修复 " + text + "：校验并补全缺失文件", 0L, 1L);
		installer._install_json(text, pyDict);
		return "已修复 " + text;
	}
}
