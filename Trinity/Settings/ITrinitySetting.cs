﻿
namespace Trinity.Config
{
    public interface ITrinitySetting<T> where T : ITrinitySetting<T>
    {
        void Reset();
        void CopyTo(T setting);
        T Clone();
    }
}
