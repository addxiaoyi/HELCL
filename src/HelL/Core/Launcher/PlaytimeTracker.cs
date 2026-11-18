namespace HelL.Core.Launcher;

public class PlaytimeTracker
{
	public string instance_name;

	public string version_id;

	private double _started;

	public PlaytimeTracker(string instance_name, string version_id)
	{
		this.instance_name = instance_name;
		this.version_id = version_id;
	}

	public void Start()
	{
		_started = Py.Time();
	}

	public long Stop()
	{
		if (_started <= 0.0)
		{
			return 0L;
		}
		long num = (long)(Py.Time() - _started);
		_started = 0.0;
		if (num > 0)
		{
			Playtime.RecordSession(instance_name, version_id, num);
		}
		return num;
	}
}
