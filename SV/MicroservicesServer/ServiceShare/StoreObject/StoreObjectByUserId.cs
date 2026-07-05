namespace ServiceShare.StoreObject
{
    public class StoreObjectByUserId<DataType> where DataType : class
    {
        public uint userId;
        public DataType data;

        public StoreObjectByUserId(uint userId, DataType data)
        {
            this.userId = userId;
            this.data = data;
        }
    }
}
