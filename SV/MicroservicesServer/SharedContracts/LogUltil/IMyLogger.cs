using System;
using System.Collections.Generic;
using System.Text;

namespace SharedContracts.LogUltil
{
    public interface IMyLogger
    {
        void Log(object msg);
        void LogWarning(object msg);
        void LogError(object msg);
    }
}
