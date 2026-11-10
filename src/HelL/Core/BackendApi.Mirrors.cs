using System;
using HelL.Core.Launcher;

namespace HelL.Core;

public partial class BackendApi
{
    public object download_source_status(bool refresh = false) => MirrorHealth.Refresh(refresh);
}
