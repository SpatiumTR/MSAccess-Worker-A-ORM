﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.OleDb;
using System.Linq;
using System.Reflection;

namespace A_ORM
{
    public static class AccesWorker
    {
        private static string _provider;

        /// <summary>
        ///     Must Be Full Path
        /// </summary>
        public static string DatabasePath { get; set; }

        /// <summary>
        ///     <para> Use if you want to change provider.</para>
        ///     <para> if you don't define it will be  Microsoft.JET.OLEDB.4.0; </para>
        /// </summary>
        public static string Provider
        {
            get => string.IsNullOrWhiteSpace(_provider) ? "Microsoft.JET.OLEDB.4.0" : _provider;
            set => _provider = value;
        }

        private static OleDbConnection Connection
        {
            get
            {
                if (string.IsNullOrWhiteSpace(DatabasePath))
                    throw new ArgumentException("DatabasePath Cannot Be Null!", nameof(DatabasePath));

                return new OleDbConnection($"Provider={Provider};" +
                                           $"data source={DatabasePath};");
            }
        }

        #region Public Methods

        /// <summary>
        /// Custom Query Object Filler
        /// </summary>
        /// <param name="DataClass">Class or Struct Fill Must be filled.</param>
        /// <param name="Query"></param>
        /// <returns></returns>
        public static List<object> GetDataList(object DataClass, string Query)
        {
            Type classType = DataClass.GetType();

            IList<PropertyInfo> props = new List<PropertyInfo>(classType.GetProperties());

            List<object> currentList = new List<object>();

            using (OleDbConnection connection = Connection)
            {
                using (OleDbCommand aCommand = new OleDbCommand(Query, connection))
                {
                    if (aCommand.Connection.State != ConnectionState.Open)
                        aCommand.Connection.Open();

                    OleDbDataReader aReader = aCommand.ExecuteReader();

                    try
                    {
                        while (aReader.Read())
                        {
                            Object instance = Activator.CreateInstance(classType);

                            instance = InstanceFiller(instance, props, aReader);

                            currentList.Add(instance);
                        }
                    }
                    catch (Exception Ex)
                    {
                        throw Ex;
                    }
                    finally
                    {
                        if (aCommand.Connection.State == ConnectionState.Open)
                            aCommand.Connection.Close();
                    }
                }

                return currentList;
            }
        }



        /// <summary>
        /// This Method will be fill the DataClass.
        /// </summary>
        /// <param name="DataClass">Class or Struct Fill Must be filled.</param>
        /// <param name="Top">0 Mean * otherwise it brings up as written.</param>
        /// <returns>Return List<object> filled to access Database</returns>
        public static List<object> List(object DataClass, int Top = 0)
        {
            Type classType = DataClass.GetType();

            string tableName = GetTableName(classType);

            string sqlQuery = SqlSelectMaker(tableName, Top);

            IList<PropertyInfo> props = new List<PropertyInfo>(classType.GetProperties());

            List<object> currentList = new List<object>();

            using (OleDbConnection connection = Connection)
            {
                using (OleDbCommand aCommand = new OleDbCommand(sqlQuery, connection))
                {
                    if (aCommand.Connection.State != ConnectionState.Open)
                        aCommand.Connection.Open();

                    OleDbDataReader aReader = aCommand.ExecuteReader();

                    try
                    {
                        while (aReader.Read())
                        {
                            // Must be in while otherwise it will be insert last instance every time.
                            object instance = Activator.CreateInstance(classType);

                            // Fill Instance
                            instance = InstanceFiller(instance, props, aReader);

                            currentList.Add(instance);
                        }
                    }
                    catch (Exception ex)
                    {
                        throw ex;
                    }
                }
            }

            return currentList;
        }

        /// <summary>
        ///     Insert Data via Class
        /// </summary>
        /// <param name="NewDatas">Class or Struct Fill Must be filled.</param>
        /// <param name="ReturnInsertedID">
        ///     Do you wan't to get inserted id after insert. Working 2 command. insert & SELECT
        ///     @@IDENTITY
        /// </param>
        /// <returns>Inserted Identity <see cref="int" /></returns>
        public static int Insert(object NewDatas, bool ReturnInsertedID = true)
        {
            QueryAndParameters Insert = SqlInsertMaker(NewDatas);

            int id = 0;

            using (OleDbConnection connection = Connection)
            {
                using (OleDbCommand aCommand = new OleDbCommand(Insert.Query, connection))
                {
                    try
                    {
                        if (aCommand.Connection.State != ConnectionState.Open)
                            aCommand.Connection.Open();

                        foreach (var parameter in Insert.Parameters)
                            aCommand.Parameters.Add(parameter);

                        aCommand.ExecuteNonQuery();

                        if (ReturnInsertedID)
                            using (OleDbCommand cmdIdentity = new OleDbCommand("SELECT @@IDENTITY", connection))
                            {
                                if (cmdIdentity.Connection.State != ConnectionState.Open)
                                    cmdIdentity.Connection.Open();

                                id = (int)cmdIdentity.ExecuteScalar();
                            }
                    }
                    catch (Exception Ex)
                    {
                        throw new ArgumentException($"Insert error, data or class wrong. Query : {Insert.Query}", Ex);
                    }
                }
            }

            return id;
        }

        /// <summary>
        ///     Update data via Class
        ///     <para> If Data dosn't update check class prop's order. Class prop's order must be same the table order.</para>
        /// </summary>
        /// <param name="NewDatas">Class with datas</param>
        public static void Update(object NewDatas)
        {
            QueryAndParameters Update = SqlUpdateMaker(NewDatas);

            using (OleDbConnection connection = Connection)
            {
                using (OleDbCommand aCommand = new OleDbCommand(Update.Query, connection))
                {
                    try
                    {
                        if (aCommand.Connection.State != ConnectionState.Open)
                            aCommand.Connection.Open();

                        foreach (OleDbParameter parameter in Update.Parameters)
                            aCommand.Parameters.Add(parameter);

                        aCommand.ExecuteNonQuery();
                    }
                    catch (Exception e)
                    {
                        throw new ArgumentException(
                            $"Update error, data or class wrong. Query : {Update.Query} | Full exception : {e}");
                    }
                }
            }
        }

        #endregion Public Methods

        #region QUERY Generators

        /// <summary>
        ///     Sql Select Query Maker
        /// </summary>
        /// <param name="TableName">Data Table</param>
        /// <param name="Top">0 Mean * otherwise it brings up as written.</param>
        /// <returns>Return Query</returns>
        private static string SqlSelectMaker(string TableName, int Top)
        {
            if (string.IsNullOrWhiteSpace(TableName))
                throw new ArgumentException("Table Name Cannot Be Null Or White Space", nameof(TableName));

            return Top == 0 ? $"Select * From {TableName}" : $"SELECT TOP {Top} * FROM {TableName}";
        }



        /// <summary>
        ///     Generate Insert Query for Class
        ///     DataObjectField(true) Mean Identity True when identity true dosn't added the query.
        ///     Tablo name MUST be the Class Name
        /// </summary>
        /// <param name="NewDatas">Class Filled datas.</param>
        /// <returns>Return Query And Paramteres for inserting.</returns>
        private static QueryAndParameters SqlInsertMaker(object NewDatas)
        {
            // Get Class Details
            Type classType = NewDatas.GetType();
            string tableName = GetTableName(classType);

            IList<PropertyInfo> props = new List<PropertyInfo>(classType.GetProperties());

            // INSERT INTO Users ([ID],[Name],[Surname],[Age]) VALUES(@ID,@Name,@Surname,@Age)
            string InsertCommand = $"INSERT INTO {tableName}";

            // Define is table columns "([title], [rating],  [review], [frnISBN], [frnUserName]) "
            string define = "";

            // values is table datas "VALUES(@ID,@Name,@Surname,@Age)"
            string values = "";

            // Get first and last prop for insert query string generating.
            PropertyInfo first = props.First();
            PropertyInfo last = props.Last();

            // Create return
            QueryAndParameters Insert = new QueryAndParameters();
            List<OleDbParameter> IP = new List<OleDbParameter>();

            // Every Property's in Class
            foreach (var prop in props)
            {
                Attribute propertyAttribute = prop.GetCustomAttribute(typeof(DisplayNameAttribute));
                string accessName = ((DisplayNameAttribute)propertyAttribute).DisplayName;

                Attribute accessDOF = prop.GetCustomAttribute(typeof(DataObjectFieldAttribute));
                bool isPrimaryKey = false;

                if (accessDOF != null)
                    isPrimaryKey = ((DataObjectFieldAttribute)accessDOF).PrimaryKey;

                string PropName = prop.Name;
                object PropValue = prop.GetValue(NewDatas);

                #region Define & Values

                if (prop.Equals(first))
                {
                    define = "(";
                    values = "VALUES(";
                }

                if (isPrimaryKey)
                    continue;

                define += $"[{accessName}]";
                values += $"@{accessName}";

                if (!prop.Equals(last))
                {
                    define += ", ";
                    values += ", ";
                }
                else
                {
                    define += ")";
                    values += ")";
                }

                #endregion Define & Values

                #region OleDbParameters

                IP.Add(new OleDbParameter($"@{PropName}", PropValue));

                #endregion OleDbParameters
            }

            InsertCommand += $" {define} {values};";

            Insert.Query = InsertCommand;
            Insert.Parameters = IP;

            return Insert;
        }

        /// <summary>
        ///     Generate Update Query for Class
        ///     DataObjectField(true) Mean Identity True when identity true added the query to where.
        ///     Tablo Name MUST Be The Class Name
        /// </summary>
        /// <param name="NewDatas">Class Filled datas.</param>
        /// <returns>Return Query And Paramteres for inserting.</returns>
        private static QueryAndParameters SqlUpdateMaker(object NewDatas)
        {
            Type classType = NewDatas.GetType();
            string tableName = GetTableName(classType);
            IList<PropertyInfo> props = new List<PropertyInfo>(classType.GetProperties());

            // INSERT INTO Users ([ID],[Name],[Surname],[Age]) VALUES(@ID,@Name,@Surname,@Age)
            string UpdateCommand = $"UPDATE {tableName} SET";

            // Define is table columns "([title], [rating],  [review], [frnISBN], [frnUserName]) "
            string define = "";

            // Get first and last prop for insert query string generating.

            PropertyInfo first = props.First();
            PropertyInfo last = props.Last();

            // Create return
            QueryAndParameters update = new QueryAndParameters();
            List<OleDbParameter> UP = new List<OleDbParameter>();
            string PrimaryKey = "";
            object PrimaryKeyVal = null;

            // Every Property's in Class
            foreach (PropertyInfo prop in props)
            {
                Attribute propertyAttribute = prop.GetCustomAttribute(typeof(DisplayNameAttribute));
                string accessName = ((DisplayNameAttribute)propertyAttribute).DisplayName;

                Attribute accessDOF = prop.GetCustomAttribute(typeof(DataObjectFieldAttribute));
                bool isPrimaryKey = false;

                if (accessDOF != null)
                    isPrimaryKey = ((DataObjectFieldAttribute)accessDOF).PrimaryKey;

                string PropName = prop.Name;
                object PropValue = prop.GetValue(NewDatas);

                #region Define & Values

                if (isPrimaryKey)
                {
                    PrimaryKey = PropName;
                    PrimaryKeyVal = PropValue;
                }
                else
                {
                    define += $"[{accessName}]=@{accessName}";
                }

                if (!prop.Equals(first) && !prop.Equals(last))
                    define += ", ";

                #endregion Define & Values

                #region OleDbParameters

                if (!isPrimaryKey) // if this is not primary key then add the array.
                    UP.Add(new OleDbParameter($"@{PropName}", PropValue));

                #endregion OleDbParameters
            }

            // Add primary key last.
            UP.Add(new OleDbParameter($"@{PrimaryKey}", PrimaryKeyVal));

            UpdateCommand += $" {define} WHERE [{PrimaryKey}]=@{PrimaryKey}";

            update.Query = UpdateCommand;
            update.Parameters = UP;

            return update;
        }

        /// <summary>
        ///     Model For the Query's
        /// </summary>
        private struct QueryAndParameters
        {
            public string Query { get; set; }
            public List<OleDbParameter> Parameters { get; set; }
        }



        #endregion QUERY Generators

        #region Helpers

        /// <summary>
        /// Get Table Name Struct Or Class
        /// <para>If Struct Get struct name to table name.</para>
        /// <para>If Class Search for display name attribute to table name.</para>
        /// </summary>
        /// <param name="classType">Which class to generate table name.</param>
        /// <returns></returns>
        private static string GetTableName(Type classType)
        {
            // Object Name == Table Name
            string tableName = classType.Name;

            // Is Class ?
            if (classType.IsClass)
            {
                // Get Class Display Name Attribute

                // Display Name Is Defined ?
                if (!(classType.GetCustomAttribute(typeof(DisplayNameAttribute)) is DisplayNameAttribute classDNA)) return tableName;

                // Get DNA Value
                var classTableName = classDNA.DisplayName;

                // Change Table Name To DNA Value If value not IsNullOrWhiteSpace
                if (!string.IsNullOrWhiteSpace(classTableName))
                    tableName = classTableName;
            }

            return tableName;
        }

        /// <summary>
        /// Fill Instance to Reader
        /// </summary>
        /// <param name="Instance">Which Object needed to fill datas</param>
        /// <param name="Props">Property's in instance</param>
        /// <param name="OdbReader">Database Reads</param>
        /// <returns>Return Full instance</returns>
        private static object InstanceFiller(object Instance, IList<PropertyInfo> Props, OleDbDataReader OdbReader)
        {
            // Loop class in prop's
            foreach (PropertyInfo prop in Props)
            {
                // Get Property
                Attribute propertyAttribute = prop.GetCustomAttribute(typeof(DisplayNameAttribute));

                // Get Property Display Name For Access
                string accessName = ((DisplayNameAttribute)propertyAttribute).DisplayName;

                // Get Data To Database
                object sqldata = OdbReader[accessName];

                // If Data Null Cannot be setted.
                if (sqldata == null || sqldata.GetType().Name == "DBNull") continue;

                // Try Set the object class in property.
                try
                {
                    prop.SetValue(Instance, Convert.ChangeType(sqldata, prop.PropertyType));
                }
                catch (Exception Ex)
                {
                    throw new ArgumentException(
                        $"Prop SetValue Exception Access Data Dosn't match to class Property Name {accessName}",
                        Ex);
                }
            }

            return Instance;
        }



        #endregion
    }
}