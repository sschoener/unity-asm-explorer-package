namespace AsmExplorer
{
    public struct MonoSourceLocation {
        public string File;
        public uint Row, Column, IlOffset;
    }
}