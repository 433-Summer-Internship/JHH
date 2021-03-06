﻿using System;
using System.Collections.Generic;
using StackExchange.Redis;

/// <summary>
/// The MikRedisDB namespace is a library whose purpose is to provide a Redis database service for a chatting application.
/// </summary>
namespace MikRedisDB
{
    /// <summary>
    /// The RedisDBController class provides a handle to a StackExchange.Redis redis-connection 
    /// as well as functions for the specific application of documenting a user pool and room pool 
    /// used in a chatting application. 
    /// </summary>
    /// <remarks>
    /// The following Redis database structures are used:
    /// <list type="definition">
    ///     <item>
    ///         <term>UserPool</term>
    ///         <description>-ZSet with User:username values and message count keys</description>
    ///     </item>
    ///     <item>
    ///         <term>User:username</term>
    ///         <description>-Hash for each username that contains user relevant data (Password, ConnectionID, LoginFlag, DummyFlag, BlockFlag, SuspendTImer, RoomNumber)</description>
    ///     </item>
    ///     <item>
    ///         <term>LoginPool</term>
    ///         <description>-Set with User:username members who's Hash contains a true LoginFlag</description>
    ///     </item>
    ///     <item>
    ///         <term>DummyPool</term>
    ///         <description>-Set with User:username members who's Hash contains a true DummyFlag</description>
    ///     </item>
    ///     <item>
    ///         <term>RoomPool</term>
    ///         <description>-ZSet with Room:roomnumber values and message count keys</description>
    ///     </item>
    ///     <item>
    ///         <term>Room:roomnumber</term>
    ///         <description>-Hash for each roomnumber that contains room relevant data (RoomTitle, RoomOwner, Server ID)</description>
    ///     </item>
    ///     <item>
    ///         <term>Room:roomnumber:Contents</term>
    ///         <description>-Set with User:username members who's contained inside the corresponding roomnumber</description>
    ///     </item>
    ///     <item>
    ///         <term>ServerPool</term>
    ///         <description>-ZSet with Server:serverID values and room count keys</description>
    ///     </item>
    ///     <item>
    ///         <term>Server:serverID</term>
    ///         <description>-Set with Room:roomnumber members who's contained inside the corresponding server</description>
    ///     </item>
    /// </list>
    /// </remarks>
    public class RedisDBController
    {
        /*****
            Methods and Properties for
            Thread-Safe Set-up, Connection, and Close
        *****/

        private static ConfigurationOptions configOptions = new ConfigurationOptions();
        private IDatabase db;

        //**Thread-safe singleton pattern for ConnectionMultiplexer**
        /// <summary>
        /// Create the Redis connection "lazily" meaning the connection won't be made until it is needed. 
        /// The Lazy class allows for thread-safe initialization.
        /// </summary>
        private static Lazy<ConnectionMultiplexer> connMulti = new Lazy<ConnectionMultiplexer>(() =>
           ConnectionMultiplexer.Connect(configOptions)
        );

        /// <summary>
        /// Read-only ConnectionMultiplexer property so it remains singleton.
        /// </summary>
        private static ConnectionMultiplexer safeConn
        {
            get
            {
                return connMulti.Value;
            }
        }

        /// <summary>
        /// The SetConfigurationOptions method allows the Redis client to setup the IP, port number, and password to the Redis server.
        /// </summary>
        /// <param name="ip">IP address of the Redis server.</param>
        /// <param name="portNumber">Port number of the Redis server.</param>
        /// <param name="password">The password for accessing the Redis Server.</param>
        public void SetConfigurationOptions (string ip, int portNumber, string password)
        {
            configOptions.EndPoints.Add(ip, portNumber);
            configOptions.Password = password;
            configOptions.ClientName = "RedisConnection";
            configOptions.KeepAlive = 200;
            configOptions.ConnectTimeout = 100000;
            configOptions.SyncTimeout = 100000;
            configOptions.AbortOnConnectFail = false;
        }

        /// <summary>
        /// The SetupConnection method retrieves a handler of the Redis database 'db' for calling Redis-DB commands.
        /// </summary>
        public void SetupConnection()
        {
            db = safeConn.GetDatabase();
        }

        /// <summary>
        /// The CloseConnection method closes the connection to the Redis server.
        /// </summary>
        public void CloseConnection()
        {
            safeConn.Close();
        }

        /*****
            Methods for
            Account Creation/Deletion/Existence/CheckValues/Retrieval/Update
        *****/

        //Create a new user account
        /// <summary>
        /// The CreateUser Method creates a new user in the DB and sets their default values.
        /// </summary>
        /// <remarks>
        /// The following information is created in the Redis database. An new entry (User:username) is made in the "UserPool" ZSet with a message count value of 0. 
        /// A Hash of the Redis-key "User:username" is created with the following hash-keys:
        /// <list type="definition">
        ///     <item>
        ///         <term>Password</term>
        ///         <description>-The user's password for login authentication.</description>
        ///     </item>
        ///     <item>
        ///         <term>ConnectionID</term>
        ///         <description>-A numeric value used to keep track of the user's connection information in the server.</description>
        ///     </item>
        ///     <item>
        ///         <term>LoginFlag</term>
        ///         <description>-A boolean value to keep track if the user is logged in.</description>
        ///     </item>
        ///     <item>
        ///         <term>DummyFlag</term>
        ///         <description>-A boolean value to keep track if the user is a dummy client.</description>
        ///     </item>
        ///     <item>
        ///         <term>BlockFlag</term>
        ///         <description>-A boolean value to keep track if the user is under suspension.</description>
        ///     </item>
        ///     <item>
        ///         <term>SuspendTimer</term>
        ///         <description>-A numeric value containing a Unix timestamp indicating the end of a suspension period. [A value of 0 = no suspension]</description>
        ///     </item>
        ///     <item>
        ///         <term>RoomNumber</term>
        ///         <description>-A numeric value indicating which room number a user is contained in. [A value of 0 = lobby]</description>
        ///     </item>
        /// </list>
        /// </remarks>
        /// <param name="name">The username of the user.</param>
        /// <param name="password">The password of the user.</param>
        /// <returns>
        /// The method returns true on success and false if the requested username already exists in the database.
        /// </returns>
        public bool CreateUser (string name, string password)
        {
            //Ensure the user does not exist
            if (!DoesUsernameExist(name))
            {
                try
                {
                    //Create a new entry for the user
                    db.SortedSetAdd("UserPool", "User:" + name, 0);         //Add new user to the userpool
                    db.HashSet("User:" + name, "Password", password);       //Add user's password
                    db.HashSet("User:" + name, "ConnectionID", 0);          //Set Connection ID default
                    db.HashSet("User:" + name, "LoginFlag", false);         //Set Login Flag default
                    db.HashSet("User:" + name, "DummyFlag", false);         //Set Dummy Flag default
                    db.HashSet("User:" + name, "BlockFlag", false);         //Set Block Flag default
                    db.HashSet("User:" + name, "SuspendTimer", 0);          //Set Suspend Timer default
                    db.HashSet("User:" + name, "RoomNumber", 0);            //Set RoomNumber to Lobby (0)
                } catch (Exception e)
                {
                    Console.WriteLine("Redis server connection error during CreateUser: " + e.Message);
                    Console.WriteLine("\t\tSource: " + e.Message);
                    return false;
                }
                return true;                                                //Creation Successful
            }
            return false;                                                   //Username already exists
        }

        //Function for deleting an account
        /// <summary>
        /// The DeleteUser method deletes a user from the Redis database.
        /// </summary>
        /// <remarks>
        /// To ensure the clean deletion of a user entry the following procedure is followed. First, the user is removed from it's room. 
        /// Next, the User:username hash is deleted. Then, the User:username entry in the "UserPool" is removed. In case the user is active within the 
        /// "LoginPool" or "DummyPool," those corresponding entries are checked and removed. Password authentication is required for this method to work.
        /// </remarks>
        /// <param name="name">The username of the user.</param>
        /// <param name="password">The password of the user.</param>
        /// <returns>
        /// This method returns true on success and false if either the username or password are incorrect (unspecified).
        /// </returns>
        public bool DeleteUser(string name, string password)
        {
            if (DoesUsernameExist(name))
            {
                if (IsPasswordCorrect(name, password))
                {
                    //Remove the user from their room
                    RoomRemoveUser((uint)db.HashGet("User:" + name, "RoomNumber"), name);
                    try
                    {
                        //Delete the User's key
                        db.KeyDelete("User:" + name);
                        db.SortedSetRemove("UserPool", "User:" + name);
                        //Be sure to remove the User name from any other pools
                        db.SetRemove("LoginPool", "User:" + name);
                        db.SetRemove("DummyPool", "User:" + name);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Redis server connection during DeleteUser: " + e.Message);
                        Console.WriteLine("\t\tSource: " + e.Message);
                        return false;
                    }
                    return true;   //Return true for success
                }
            }
            return false;          //Username or Password is incorrect
        }

        //***Check Value Functions***

        //Check if a particular user account exists
        /// <summary>
        /// The DoesUsernameExist method determines if a user is contained within the Redis database.
        /// </summary>
        /// <param name="name">The username of the user.</param>
        /// <returns>The method returns true if the user exists and false if the user does not exist.</returns>
        public bool DoesUsernameExist (string name)
        {
            try
            {
                if (db.KeyExists("User:" + name))
                {
                    return true;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during DoesUsernameExist: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return false;
            }
            return false;
        }

        //Check if password is correct
        /// <summary>
        /// The IsPasswordCorrect method determines if a user's password is correct.
        /// </summary>
        /// <remarks>
        /// This method is set to private to prevent a RedisDBController caller from determining if a password is correct.
        /// </remarks>
        /// <param name="name">The username of the user.</param>
        /// <param name="password">The password of the user.</param>
        /// <returns>
        /// The method returns true if the password is correct and false if the password is incorrect.
        /// </returns>
        private bool IsPasswordCorrect (string name, string password)
        {
            try
            {
                if (db.HashGet("User:" + name, "Password") == password)
                {
                    return true;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during IsPasswordCorrect: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return false;
            }
            return false;
        }

        //Check if a user is logged in
        /// <summary>
        /// The IsUserLoggedIn method determines if the user is logged in or not. Return values: (-2) Redis connection failure; (-1) Username doesn't exist; 
        /// (0) User not logged in; (1) User logged in.
        /// </summary>
        /// <param name="name">The username of the user.</param>
        /// <returns>
        /// The method returns an integer value to report one of 4 cases:
        /// <list type="definition">
        ///     <item>
        ///         <term>-2</term>
        ///         <description>-The connection to the Redis service has failed.</description>
        ///     </item>
        ///     <item>
        ///         <term>-1</term>
        ///         <description>-The username does not exist.</description>
        ///     </item>
        ///     <item>
        ///         <term>0</term>
        ///         <description>-The user is not logged in.</description>
        ///     </item>
        ///     <item>
        ///         <term>1</term>
        ///         <description>-The user is logged in.</description>
        ///     </item>
        /// </list>
        /// </returns>
        public int IsUserLoggedIn (string name)
        {
            if (DoesUsernameExist(name))
            {
                try
                {
                    if (db.HashGet("User:" + name, "LoginFlag") == true)
                    {
                        return 1;       //User is logged in
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Redis server connection error during IsUserLoggedIn: " + e.Message);
                    Console.WriteLine("\t\tSource: " + e.Message);
                    return -2;          //Return -2 for a Redis connection error
                }
                return 0;               //User is not logged in
            }
            return -1;                  //Username does not exist
        }

        //Check if a user is a dummy
        /// <summary>
        /// The IsUserDummy method checks if a user is a dummy client. Return values: (-2) Redis connection failure; (-1) Username doesn't exist; (0) User not dummy; (1) User is dummy.
        /// </summary>
        /// <param name="name">The username of the user.</param>
        /// <returns>
        /// The method returns an integer value to report one of 4 cases:
        /// <list type="definition">
        ///     <item>
        ///         <term>-2</term>
        ///         <description>-The connection to the Redis service has failed.</description>
        ///     </item>
        ///     <item>
        ///         <term>-1</term>
        ///         <description>-The username does not exist.</description>
        ///     </item>
        ///     <item>
        ///         <term>0</term>
        ///         <description>-The user is not a dummy.</description>
        ///     </item>
        ///     <item>
        ///         <term>1</term>
        ///         <description>-The user is a dummy.</description>
        ///     </item>
        /// </list>
        /// </returns>
        public int IsUserDummy (string name)
        {
            if (DoesUsernameExist(name))
            {
                try
                {
                    if (db.HashGet("User:" + name, "DummyFlag") == true)
                    {
                        return 1;       //User is a dummy
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Redis server connection error during IsUserDummy: " + e.Message);
                    Console.WriteLine("\t\tSource: " + e.Message);
                    return -2;          //Return -2 for a Redis connection error
                }
                return 0;               //User is not a dummy
            }
            return -1;                  //Username does not exist
        }

        //Check if a user is blocked
        /// <summary>
        /// The IsUserBlocked method checks if a user is suspended. Return values: (-2) Redis connection failure; (-1) Username doesn't exist; 
        /// (0) User not suspended; (1) User suspended; (2) User no longer suspended.
        /// </summary>
        /// <remarks>
        /// Suspension times for blocked users are only updated upon a relevant DB check. This method reports that a previously unchecked suspension has ended 
        /// by returning a value of 2.
        /// </remarks>
        /// <param name="name">The username of the user.</param>
        /// <returns>
        /// The method returns an integer value to report one of 5 cases:
        /// <list type="definition">
        ///     <item>
        ///         <term>-2</term>
        ///         <description>-The connection to the Redis service has failed.</description>
        ///     </item>
        ///     <item>
        ///         <term>-1</term>
        ///         <description>-The username does not exist.</description>
        ///     </item>
        ///     <item>
        ///         <term>0</term>
        ///         <description>-The user is not suspended.</description>
        ///     </item>
        ///     <item>
        ///         <term>1</term>
        ///         <description>-The user is suspended.</description>
        ///     </item>
        ///     <item>
        ///         <term>2</term>
        ///         <description>-The user is no longer suspended.</description>
        ///     </item>
        /// </list>
        /// </returns>
        public int IsUserBlocked (string name)
        {
            if (DoesUsernameExist(name))
            {
                try
                {
                    if (db.HashGet("User:" + name, "BlockFlag") == true)
                    {
                        if (HasSuspensionEnded(name))
                        {
                            return 2;   //Return 2 to say the user is no longer blocked   
                        }
                        return 1;       //User is blocked
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Redis server connection error during IsUserBlocked: " + e.Message);
                    Console.WriteLine("\t\tSource: " + e.Message);
                    return -2;          //Return -2 for a Redis connection error
                }
                return 0;               //User is not blocked
            }
            return -1;                  //Username does not exist
        }

        //***Data Retrieval Functions***

        //Method to get connection ID
        /// <summary>
        /// The GetUsrConnectionID method returns the connection ID of the user. Return values: (-2) Redis connection failure; (-1) Username doesn't exist; 
        /// (Other values) (int)connectionID.
        /// </summary>
        /// <param name="name">The username of the user.</param>
        /// <returns>
        /// The method returns -1 when the passed username does not exist and -2 upon Redis connection failure. If the user exists, the connectionID is returned as an int.
        /// </returns>
        public int GetUserConnectionID (string name)
        {
            if (DoesUsernameExist(name))
            {
                try
                {
                    return (int)db.HashGet("User:" + name, "ConnectionID");     //Return Connection ID
                }
                catch (Exception e)
                {
                    Console.WriteLine("Redis server connection error during GetUserConnectionID: " + e.Message);
                    Console.WriteLine("\t\tSource: " + e.Message);
                    return -2;                                                  //Return -2 for a Redis connection error
                }
            }
            return -1;                                                          //Username does not exist
        }

        //Method to get a blocked user's suspend time
        /// <summary>
        /// The GetUserSuspendTime method returns a user's suspension timestamp if the user is suspended. Return values: (0) Username doesn't exist; (1) User not suspended; 
        /// (2) User no longer suspended; (3) Redis connection failure; (Other Values) (uint)Suspension expiration timestamp in Unix time.
        /// </summary>
        /// <remarks>
        /// Suspension times for blocked users are only updated upon a relevant DB check. This method reports that a previously unchecked suspension has ended 
        /// by returning a value of 2.
        /// </remarks>
        /// <param name="name">The username of the user.</param>
        /// <returns>
        /// The method returns an unsigned integer value to report one of 5 cases:
        /// <list type="definition">
        ///     <item>
        ///         <term>0</term>
        ///         <description>-The username does not exist.</description>
        ///     </item>
        ///     <item>
        ///         <term>1</term>
        ///         <description>-The user is not suspended.</description>
        ///     </item>
        ///     <item>
        ///         <term>2</term>
        ///         <description>-The user is no longer suspended.</description>
        ///     </item>
        ///     <item>
        ///         <term>3</term>
        ///         <description>-The connection to the Redis service has failed.</description>
        ///     </item>
        ///     <item>
        ///         <term>Other Values</term>
        ///         <description>-A uint representation of the suspension expiration Unix timestamp.</description>
        ///     </item>
        /// </list>
        /// </returns>
        public uint GetUserSuspendTime (string name)
        {
            if (DoesUsernameExist(name))
            {
                int check = IsUserBlocked(name);
                switch (check)
                {
                    case 1:
                        try
                        {
                            return (uint)db.HashGet("User:" + name, "SuspendTimer");        //Return penalty expiration timestamp
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("Redis server connection error during CreateUser: " + e.Message);
                            Console.WriteLine("\t\tSource: " + e.Message);
                            return 3;
                        }
                    case 2:
                        return 2;                                                           //Return 2 to say the user is no longer blocked
                    case 0:
                        return 1;                                                           //Return 1 for user is not blocked
                    case -2:
                        return 3;                                                           //Return 3 for a Redis connection error (from IsUserBlocked)
                }
            }
            return 0;                                                                       //Username does not exist
        }

        //Private function to check if a suspend time has expired
        //If the time has expired, this function unblocks the user
        /// <summary>
        /// The HasSuspensionEnded method checks if a user's suspension time has ended.
        /// </summary>
        /// <remarks>
        /// This method is private as it is intended to only be used by suspension-relevant public functions in RedisDBController.
        /// </remarks>
        /// <param name="name">The username of the user.</param>
        /// <returns>
        /// The method returns true if the user is no longer blocked and false if the user is still blocked.
        /// </returns>
        private bool HasSuspensionEnded (string name)
        {
            uint timePenalty = (uint)db.HashGet("User:" + name, "SuspendTimer");                        //Get the timestamp when the penalty expires
            uint timeNow = (uint)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;     //Get the current Unix time
            if (timePenalty <= timeNow)
            {
                try
                {
                    //Unblock-user
                    db.HashSet("User:" + name, "BlockFlag", false);                                         //Unset Block Flag
                    db.HashSet("User:" + name, "SuspendTimer", 0);                                          //Set the duration of the block back to 0  
                }
                catch (Exception e)
                {
                    Console.WriteLine("Redis server connection error during HasSuspensionEnded: " + e.Message);
                    Console.WriteLine("\t\tSource: " + e.Message);
                    return false;
                }
                return true;                                                                            //Return true to say the user is no longer blocked   
            }
            return false;                                                                               //Return false to say the user is still blocked
        }

        //Function to get a user's message count
        /// <summary>
        /// The GetUserMessageCount method retrieves the user's message count. Return values: (-2) Redis connection failure; (-1) Username doesn't exist; (Other values) (int)message count.
        /// </summary>
        /// <param name="name">The username of the user.</param>
        /// <returns>
        /// The method returns -1 if the user does not exist, -2 for a Redis connection failure, and the message count as an int if the user exists.
        /// </returns>
        public int GetUserMessageCount (string name)
        {
            if (DoesUsernameExist(name))
            {
                try
                {
                    return (int)db.SortedSetScore("UserPool", "User:" + name);  //Return user's message count
                }
                catch (Exception e)
                {
                    Console.WriteLine("Redis server connection error during GetUserMessageCount: " + e.Message);
                    Console.WriteLine("\t\tSource: " + e.Message);
                    return -2;                                                  //Return -2 for a Redis connection error
                }
            }
            return -1;                                                          //Username does not exist
        }

        //Method to get user's room location
        /// <summary>
        /// The GetUserLocation method retrieves the user's room location. Return values: (-3) Redis connection failure; (-2) User is not logged in; (-1) Username doesn't exist; (Other values) (int)room number [0 = Lobby].
        /// </summary>
        /// <param name="name">The username of the user.</param>
        /// <returns>
        /// The method returns -1 if the user does not exist, -2 if the user is not logged in, -3 on a Redis connection failure, and the room number as an int if the user exists.
        /// </returns>
        public int GetUserLocation (string name)
        {
            if (IsUserLoggedIn(name) == 0)
            {
                return -2;                                                      //If user is not logged in, report -2 for "no location"
            }
            if (DoesUsernameExist(name))
            {
                try
                {
                    return ((int)db.HashGet("User:" + name, "RoomNumber"));     //Return user's room number
                }
                catch (Exception e)
                {
                    Console.WriteLine("Redis server connection error during GetUserLocation: " + e.Message);
                    Console.WriteLine("\t\tSource: " + e.Message);
                    return -3;                                                  //Return -3 for a Redis connection error
                }
            }
            return -1;                                                          //Return -1 if the user does not exist
        }

        //Method to get User's rank 
        /// <summary>
        /// The GetUserRank method retrieves the user's message count rank. Return values: (-2) Redis connection failure; (-1) Username doesn't exist; (Other values) (int)rank.
        /// </summary>
        /// <remarks>
        /// The Redis ZSet ordering starts at index 0 and we add one to the index to get the properly ordered rank.
        /// </remarks>
        /// <param name="name">The username of the user.</param>
        /// <returns>
        /// The method returns -1 if the user does not exist, -2 on a Redis connection failure and the rank as an int if the user exists.
        /// </returns>
        public int GetUserRank (string name)
        {
            if (DoesUsernameExist(name))
            {
                try
                {
                    return ((int)db.SortedSetRank("UserPool", "User:" + name, Order.Descending) + 1);       //Return user's rank (in descending order) [+1 because the index starts at 0]
                }
                catch (Exception e)
                {
                    Console.WriteLine("Redis server connection error during GetUserRank: " + e.Message);
                    Console.WriteLine("\t\tSource: " + e.Message);
                    return -2;                                                                              //Return -2 for a Redis connection error
                }
            }
            return -1;                                                                                      //Username does not exist
        }

        //Get the message count of a certain rank
        /// <summary>
        /// The GetMessageCountAtRank method retrieves the message count at a specified rank. Return values: (-2) Redis connection failure; (-1) Rank doesn't exist; 
        /// (Other values) (int)message count.
        /// </summary>
        /// <remarks>
        /// This method finds the message count rank in the "UserPool" by using the StackExchange.Redis method called SortedSetRangeByRank and looking up the Redis value at the 
        /// (rank - 1) index. After obtaining the Redis value, the message count is determined by using SortedSetScore.
        /// </remarks>
        /// <param name="rank">An int representing the rank.</param>
        /// <returns>
        /// The method returns -1 if the rank does not exist, -2 on a Redis connection failure, and the message count at rank as an int if the rank exists.
        /// </returns>
        public int GetMessageCountAtRank (int rank)
        {
            try
            {
                if (rank <= db.SortedSetLength("UserPool"))                                     //Check if the rank value is within range
                {
                    //Return the score at the value which is the at the value at the position given by the specified rank position
                    return (int)db.SortedSetScore("UserPool", db.SortedSetRangeByRank("UserPool", 0, rank - 1, Order.Descending)[rank - 1]);
                }
                return -1;                                                                      //Rank does not exist
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during GetMessageCountAtRank: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return -2;                                                                      //Return -2 for a Redis connection error
            }                                                                                   
        }

        //Get the username of a certain rank
        /// <summary>
        /// The GetUsernameAtRank method retrieves the username at a specified rank. Return values: ("1") Redis connection failure; ("0") Rank doesn't exist; (Other values) (string)Username.
        /// </summary>
        /// <remarks>
        /// This method finds the message count rank in the "UserPool" by using the StackExchange.Redis method called SortedSetRangeByRank and looking up the Redis value at the 
        /// (rank - 1) index. The Redis value is the username.
        /// </remarks>
        /// <param name="rank">An int representing the rank.</param>
        /// <returns>
        /// The method returns the string "0" if the rank does not exist, "1" on a Redis connection failure, and the username at rank as a string if the rank exists.
        /// </returns>
        public string GetUsernameAtRank(int rank)
        {
            try
            {
                if (rank <= db.SortedSetLength("UserPool"))                                                 //Check if the rank value is within range
                {
                    //Return the score at the value which is the at the value at the position given by the specified rank position
                    return db.SortedSetRangeByRank("UserPool", 0, rank - 1, Order.Descending)[rank - 1];
                }
                return "0";                                                                                 //Rank does not exist
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during GetUsernameAtRank: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return "1";                                                                                 //Return -2 for a Redis connection error
            }
        }

        //***Data Set Functions***

        //Method to change username
        /// <summary>
        /// The ChangeUsername method attempts a change of a username. Return values: (-4) Redis connection failure; (-3) Name taken; (-2) currentName = newName; 
        /// (-1); User blocked; (0) Auth error; (1) Success.
        /// </summary>
        /// <remarks>
        /// Upon username change, a variety of Redis DB entries must be changed to avoid errors in the database. 
        /// </remarks>
        /// <param name="currentName">The current username of the user.</param>
        /// <param name="newName">The desired username of the user.</param>
        /// <param name="password">The password of the user.</param>
        /// <returns>
        /// The method returns and int to distinguish between various result types:
        /// <list type="definition">
        ///     <item>
        ///         <term>-4</term>
        ///         <description>-The connection with the Redis server failed.</description>
        ///     </item>
        ///     <item>
        ///         <term>-3</term>
        ///         <description>-Authentication successful, but the desired name is already taken.</description>
        ///     </item>
        ///     <item>
        ///         <term>-2</term>
        ///         <description>-Authentication successful, but the desired name is the same as the current name.</description>
        ///     </item>
        ///     <item>
        ///         <term>-1</term>
        ///         <description>-Authentication successful, but the user is suspended.</description>
        ///     </item>
        ///     <item>
        ///         <term>0</term>
        ///         <description>-The username or password is incorrect (unspecified).</description>
        ///     </item>
        ///     <item>
        ///         <term>1</term>
        ///         <description>-The username was successfully changed.</description>
        ///     </item>
        /// </list>
        /// </returns>
        public int ChangeUsername (string currentName, string newName, string password)
        {
            if (DoesUsernameExist(currentName))
            {
                if (IsPasswordCorrect(currentName, password))
                {
                    //Check if user is not blocked
                    int check = IsUserBlocked(currentName);
                    switch (check)
                    {
                        case 0:
                        case 2:
                            //Check if the new name is different than the old one
                            if (currentName == newName)
                            {
                                return -2;                                                                  //If the two names are the same, return -2
                            }
                            if (DoesUsernameExist(newName))
                            {
                                return -3;                                                                  //If the new name is already used, return -3
                            }
                            uint tempNumber = 0;
                            try
                            {
                                tempNumber = (uint)db.HashGet("User:" + currentName, "RoomNumber");
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Redis server connection error during ChangeUsername: " + e.Message);
                                Console.WriteLine("\t\tSource: " + e.Message);
                                return -4;                                                                  //Return -4 for a Redis connection error
                            }
                            RoomRemoveUser(tempNumber, currentName);                                        //Remove the user's name from the current room
                            RoomAddUser(tempNumber, newName);                                               //Add the user's name from the current room

                            try
                            {
                                db.KeyRename("User:" + currentName, "User:" + newName);                     //At this point all the credentials have been cleared and we can now update the user's name
                                tempNumber = (uint)db.SortedSetScore("UserPool", "User:" + currentName);    //Temporarily hold the user's score
                                db.SortedSetAdd("UserPool", "User:" + newName, tempNumber);                 //Create the new user entry
                                db.SortedSetRemove("UserPool", "User:" + currentName);                      //Remove the old entry

                                //If logged in or dummy, need to update sets
                                if (db.SetContains("LoginPool", "User:" + currentName))
                                {
                                    db.SetRemove("LoginPool", "User:" + currentName);
                                    db.SetAdd("LoginPool", "User:" + newName);
                                }
                                if (db.SetContains("DummyPool", "User:" + currentName))
                                {
                                    db.SetRemove("DummyPool", "User:" + currentName);
                                    db.SetAdd("DummyPool", "User:" + newName);
                                }
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Redis server connection error during ChangeUsername: " + e.Message);
                                Console.WriteLine("\t\tSource: " + e.Message);
                                return -4;                                                                  //Return -4 for a Redis connection error
                            }
                            return 1;                                                                       //Return 1 if successful
                        default:
                            return -1;                                                                      //User is blocked
                    }                                                                                   
                }
            }
            return 0;                                                                                       //Username or Password is incorrect
        }

        //Method to change password
        /// <summary>
        /// The ChangePassword method attempts a change of a password. Return values: (-3) Redis connection failure; (-2) currentPassword = newPassword; (-1); User blocked; 
        /// (0) Auth error; (1) Success.
        /// </summary>
        /// <param name="name">The username of the user.</param>
        /// <param name="currentPassword">The current password of the user.</param>
        /// <param name="newPassword">The desired password of the user.</param>
        /// <returns>
        /// The method returns and int to distinguish between various result types:
        /// <list type="definition">
        ///     <item>
        ///         <term>-3</term>
        ///         <description>-The connection with the Redis server failed.</description>
        ///     </item>
        ///     <item>
        ///         <term>-2</term>
        ///         <description>-Authentication successful, but the desired password is the same as the current password.</description>
        ///     </item>
        ///     <item>
        ///         <term>-1</term>
        ///         <description>-Authentication successful, but the user is suspended.</description>
        ///     </item>
        ///     <item>
        ///         <term>0</term>
        ///         <description>-The username or password is incorrect (unspecified).</description>
        ///     </item>
        ///     <item>
        ///         <term>1</term>
        ///         <description>-The password was successfully changed.</description>
        ///     </item>
        /// </list>
        /// </returns>
        public int ChangePassword (string name, string currentPassword, string newPassword)
        {
            if (DoesUsernameExist(name))
            {
                if (IsPasswordCorrect(name, currentPassword))
                {
                    //Check if user is not blocked
                    int check = IsUserBlocked(name);
                    switch (check)
                    {
                        case 0:
                        case 2:
                            //Check if the new name is different than the old one
                            if (currentPassword == newPassword)
                            {
                                return -2;                                                      //If the two passwords are the same, return -2
                            }
                            try
                            {
                                db.HashSet("User:" + name, "Password", newPassword);            //At this point all the credentials have been cleared and we can now update the user's password
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Redis server connection error during ChangePassword: " + e.Message);
                                Console.WriteLine("\t\tSource: " + e.Message);
                                return -3;                                                      //Return -3 for a Redis connection error
                            }
                            return 1;                                                           //Return 1 if successful
                        default:
                            return -1;                                                          //User is blocked
                    }
                }
            }
            return 0;                                                                           //Username or Password is incorrect (0)
        }

        //Method to change Connection ID
        //Returns old connection ID
        /// <summary>
        /// The ChangeConnectionID method attempts to change the user's connection ID. Return values: (-2) Redis connection failure; (-1) Username doesn't exist; 
        /// (Other values) (int)PREVIOUS connection ID.
        /// </summary>
        /// <param name="name">The username of the user.</param>
        /// <param name="newConnectionID">The new connection ID of the user.</param>
        /// <returns>
        /// The method returns -1 if the user does not exist, -2 on a Redis connection failure, and the PREVIOUS connection ID as an int if the user exists.
        /// </returns>
        public int ChangeConnectionID (string name, int newConnectionID)
        {
            if (DoesUsernameExist(name))
            {
                int oldConnectionID = GetUserConnectionID(name);                    //Set the reporting ID to the old connection ID
                try
                {
                    db.HashSet("User:" + name, "ConnectionID", newConnectionID);    //Set new Connection ID
                }
                catch (Exception e)
                {
                    Console.WriteLine("Redis server connection error during ChangeConnectionID: " + e.Message);
                    Console.WriteLine("\t\tSource: " + e.Message);
                    return -2;                                                      //Return -2 for a Redis connection error
                }
                return oldConnectionID;                                             //Return the old connection ID
            }
            return -1;                                                              //Username does not exist
        }

        //Method to set a block/suspension
        /// <summary>
        /// The BlockUser sets a suspension (in minutes) on a particular user. Return values: (0) Username doesn't exist; (Other Values) (1) Redis connection failure; 
        /// (uint)Suspension expiration timestamp in Unix time.
        /// </summary>
        /// <remarks>
        /// Suspension times for blocked users are only updated upon a relevant DB check.
        /// </remarks>
        /// <param name="name">The username of the user.</param>
        /// <param name="minutes">The number of minutes the user will be suspended for.</param>
        /// <returns>
        /// The method returns an unsigned integer value to report one of 4 cases:
        /// <list type="definition">
        ///     <item>
        ///         <term>0</term>
        ///         <description>-The username does not exist.</description>
        ///     </item>
        ///     <item>
        ///         <term>1</term>
        ///         <description>-The connection with the Redis server failed.</description>
        ///     </item>
        ///     <item>
        ///         <term>Other Values</term>
        ///         <description>-A uint representation of the suspension expiration Unix timestamp.</description>
        ///     </item>
        /// </list>
        /// </returns>
        public uint BlockUser (string name, uint minutes)
        {
            if (DoesUsernameExist(name))
            {
                uint unixTime = 0;
                if (IsUserBlocked (name) == 1)
                {
                    try
                    {
                        unixTime = (uint)db.HashGet("User:" + name, "SuspendTimer");                            //If blocked, get the current suspend time
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Redis server connection error during BlockUser: " + e.Message);
                        Console.WriteLine("\t\tSource: " + e.Message);
                        return 1;					                                                            //Return 1 for a Redis connection error
                    }
                } else
                {
                    unixTime = (uint)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;         //Otherwise, get the current Unix time
                }
                unixTime = unixTime + (minutes * 60);                                                           //Add the new penalty to the time
                try
                {
                    db.HashSet("User:" + name, "SuspendTimer", unixTime);                                       //Set the penalty timeout time
                    db.HashSet("User:" + name, "BlockFlag", true);                                              //Set Block Flag
                }
                catch (Exception e)
                {
                    Console.WriteLine("Redis server connection error during BlockUser: " + e.Message);
                    Console.WriteLine("\t\tSource: " + e.Message);
                    return 1;                                                                                   //Return 1 for a Redis connection error
                }
                return unixTime;                                                                                //Return the time for success
            }
            return 0;                                                                                           //Username does not exist
        }

        //Method to unblock a user
        /// <summary>
        /// The UnBlockUser method removes a suspension on a user. Return values: (-2) Redis connection failure; (-1) Username isn't suspended; (0) Username doesn't exist; (1) User unsuspended.
        /// </summary>
        /// <remarks>
        /// Suspension times for blocked users are only updated upon a relevant DB check. 
        /// </remarks>
        /// <param name="name">The username of the user.</param>
        /// <returns>
        /// The method returns an integer value to report one of 4 cases:
        /// <list type="definition">
        ///     <item>
        ///         <term>-2</term>
        ///         <description>-The connection with the Redis server failed.</description>
        ///     </item>
        ///     <item>
        ///         <term>-1</term>
        ///         <description>-The user is not suspended.</description>
        ///     </item>
        ///     <item>
        ///         <term>0</term>
        ///         <description>-The username does not exist.</description>
        ///     </item>
        ///     <item>
        ///         <term>1</term>
        ///         <description>-The user is unsuspended.</description>
        ///     </item>
        /// </list>
        /// </returns>
        public int UnBlockUser(string name)
        {
            if (DoesUsernameExist(name))
            {
                if (IsUserBlocked(name) == 0)                                       //If the user isn't blocked
                {
                    return -1;                                                      //Return -1
                }
                try
                {
                    db.HashSet("User:" + name, "BlockFlag", false);                 //Unset Block Flag
                    db.HashSet("User:" + name, "SuspendTimer", 0);                  //Set the duration of the block back to 0
                }
                catch (Exception e)
                {
                    Console.WriteLine("Redis server connection error during UnBlockUser: " + e.Message);
                    Console.WriteLine("\t\tSource: " + e.Message);
                    return -2;                                                      //Return -2 for a Redis connection error
                }
                return 1;                                                           //Return 1 for success
            }
            return 0;                                                               //Username does not exist, return 0
        }

        //Method to increment users message count
        /// <summary>
        /// The AddToUserMessageCount method increases (or decreases) a users message count. Return values: (-2) Redis connection failure; (-1) Username doesn't exist; 
        /// (Other Values) User's new message count.
        /// </summary>
        /// <remarks>
        /// This function allows the message count to be decremented with a negative input value. However, the message count cannot go below 0 and this is accounted for in the method. 
        /// </remarks>
        /// <param name="name">The username of the user.</param>
        /// <param name="count">An int amount to increase or decrease the message count of a user.</param>
        /// <returns>
        /// The method returns an integer value to report one of 3 cases:
        /// <list type="definition">
        ///     <item>
        ///         <term>-2</term>
        ///         <description>-The connection with the Redis server failed.</description>
        ///     </item>
        ///     <item>
        ///         <term>-1</term>
        ///         <description>-The username does not exist.</description>
        ///     </item>
        ///     <item>
        ///         <term>Other Values</term>
        ///         <description>-The user's new message count after modification (returned as an int).</description>
        ///     </item>
        /// </list>
        /// </returns>
        public int AddToUserMessageCount (string name, int count)
        {
            if (DoesUsernameExist(name))
            {
                int currentMessageCount = 0;
                try
                {
                    currentMessageCount = (int)db.SortedSetScore("UserPool", "User:" + name);       //Get the current message count
                }
                catch (Exception e)
                {
                    Console.WriteLine("Redis server connection error during AddToUserMessageCount: " + e.Message);
                    Console.WriteLine("\t\tSource: " + e.Message);
                    return -2;                                                                      //Return -2 for a Redis connection error
                }
                currentMessageCount += count;                                                       //Increment by the desired value
                if (currentMessageCount < 0)                                                        //If the new value is less than zero, set to zero
                {
                    currentMessageCount = 0;
                }
                try
                {
                    db.SortedSetAdd("UserPool", "User:" + name, currentMessageCount);               //Update the user's message count
                }
                catch (Exception e)
                {
                    Console.WriteLine("Redis server connection error during AddToUserMessageCount: " + e.Message);
                    Console.WriteLine("\t\tSource: " + e.Message);
                    return -2;                                                                      //Return -2 for a Redis connection error
                }
                return currentMessageCount;                                                         //Return user's new message count
            }
            return -1;                                                                              //Username does not exist
        }

        /*****
            Methods for
            Account Login/Logout
        *****/

        //Method for log-in
        /// <summary>
        /// The Login method attempts to login a user. Return values: (-2) User is suspended; (-1) User is suspended; (0) Auth error; 
        /// (Other values) (int)PREVIOUS connection ID [login override] or connection ID [normal case].
        /// </summary>
        /// <remarks>
        /// If a login occurs when a user is already logged in, a login override occurs. In such a case, the new connection ID is different than the previous one. 
        /// The returned connection ID is then the previous connection ID, which allows a server to determine quickly if the login request produced a login override.
        /// </remarks>
        /// <param name="name">The username of the user.</param>
        /// <param name="password">The password of the user.</param>
        /// <param name="connectionID">The connection ID of the user.</param>
        /// <param name="isDummy">A flag; true if the user is a dummy and false otherwise (default).</param>
        /// <returns>
        /// The method returns -1 if the user is suspended, the connection ID if the user wasn't previously logged in and the PREVIOUS connection ID as an int in case of a login override.
        /// </returns>
        public int Login (string name, string password, int connectionID, bool isDummy = false)
        {
            if (DoesUsernameExist(name))
            {
                if (IsPasswordCorrect (name, password))
                {
                    //Check if user is blocked
                    int check = IsUserBlocked(name);
                    switch (check)
                    {
                        case 0:
                        case 2:
                            int oldConnectionID = connectionID;                             //By default, set the reporting ID variable to the new connectionID
                            if (IsUserLoggedIn(name) == 1)
                            {
                                oldConnectionID = GetUserConnectionID(name);                //If the user is already logged in, then change the reporting ID to the old connection ID
                            }
                            try
                            {

                                db.HashSet("User:" + name, "LoginFlag", true);              //Set the Login-Flag to true
                                db.HashSet("User:" + name, "ConnectionID", connectionID);   //Set Dummy Flag and Connection ID
                                db.HashSet("User:" + name, "DummyFlag", isDummy);           //Set Dummy Flag and Connection ID
                                db.HashSet("User:" + name, "RoomNumber", 0);                //Set RoomNumber to Lobby (0)
                                db.SetAdd("LobbyPool", "User:" + name);                     //Add user from the lobby pool
                                if (isDummy == true)
                                {
                                    db.SetAdd("DummyPool", "User:" + name);                 //Keep track of logged in dummy users for stats
                                }
                                else
                                {
                                    db.SetAdd("LoginPool", "User:" + name);                 //Keep track of logged in real users for stats
                                }
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Redis server connection error during Login: " + e.Message);
                                Console.WriteLine("\t\tSource: " + e.Message);
                                return -2;                                                  //Return -2 for a Redis connection error
                            }
                            return connectionID;                                            //Return connectionID if successful 
                        default:
                            return -1;                                                      //User is blocked
                    }
                }
            }
            return 0;                                                                       //Username or Password is incorrect
        }

        //Method for logging out
        /// <summary>
        /// The Logout method logs a user out. Return values: (-2) User is suspended; (-1) User does not exist; (0) User already logged out; (1) Success.
        /// </summary>
        /// <param name="name">The username of the user.</param>
        /// <returns>
        /// The method returns an integer value to report one of 3 cases:
        /// <list type="definition">
        ///     <item>
        ///         <term>-2</term>
        ///         <description>-The connection with the Redis server failed.</description>
        ///     </item>
        ///     <item>
        ///         <term>-1</term>
        ///         <description>-The username does not exist.</description>
        ///     </item>
        ///     <item>
        ///         <term>0</term>
        ///         <description>-The user is already logged out.</description>
        ///     </item>
        ///     <item>
        ///         <term>1</term>
        ///         <description>-Logout successful.</description>
        ///     </item>
        /// </list>
        /// </returns>
        public int Logout (string name)
        {
            if (DoesUsernameExist(name))
            {
                if (IsUserLoggedIn(name) == 0)
                {
                    return 0;                                                           //If the user isn't logged in, report 0 and do nothing
                }
                //Reset relevant data
                try
                {
                    if (IsUserDummy(name) == 1)
                    {
                        db.SetRemove("DummyPool", "User:" + name);                      //Keep track of dummy users for stats
                    }
                    else
                    {
                        db.SetRemove("LoginPool", "User:" + name);                      //Keep track of logged in users for stats
                    }
                    db.HashSet("User:" + name, "LoginFlag", false);                     //Set the Login-Flag to true
                    db.HashSet("User:" + name, "ConnectionID", 0);                      //Set Dummy Flag and Connection ID
                    db.HashSet("User:" + name, "RoomNumber", 0);                        //Set RoomNumber to Lobby (0)
                    db.HashSet("User:" + name, "DummyFlag", false);                     //Set Dummy Flag and Connection ID
                    int location = GetUserLocation(name);
                    if (location == 0)
                    {
                        db.SetRemove("LobbyPool", "User:" + name);                      //Remove user from the lobby pool
                    }
                    else
                    {
                        db.SetRemove("Room:" + location + ":Contents", "User:" + name); //Remove user from the room they are in
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Redis server connection error during Logout: " + e.Message);
                    Console.WriteLine("\t\tSource: " + e.Message);
                    return -2;                                                          //Return -2 for a Redis connection error
                }
                return 1;                                                               //Logout successful
            }
            return -1;                                                                  //Username incorrect
        }

        /*****
            Methods for
            Database Statistics
        *****/

        //Method for getting size of UserPool
        /// <summary>
        /// The GetUserPoolSize method returns the total number of users registered in the database.
        /// </summary>
        /// <returns>
        /// The method returns an long value of the number of registered users in the database. Returns -1 on a Redis connection error.
        /// </returns>
        public long GetUserPoolSize ()
        {
            try
            {
                return db.SortedSetLength("UserPool");
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during GetUserPoolSize: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return -1;                   //Return -1 for a Redis connection error
            }
        }

        //Method to get all users
        /// <summary>
        /// The GetUserList method returns a string[] of all the names of users registered in the database.
        /// </summary>
        /// <returns>
        /// The method returns a string[] of all the names of users registered in the database. Returns null on a Redis connection error.
        /// </returns>
        public string[] GetUserList ()
        {
            try
            {
                return db.SortedSetRangeByRank("UserPool", 0, -1, Order.Descending).ToStringArray();
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during GetUserList: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return null;                   //Return null for a Redis connection error
            }
        }

        //Method to get rank list
        //Generate a top-n list from the UserPool
        /// <summary>
        /// The GetTopList method returns a Dictionary containing the top-n ranking list of the top n-ranked users in the database.
        /// </summary>
        /// <param name="topNumber">The top-n number of rank entries desired to be examined.</param>
        /// <returns>
        /// This method returns a Dictionary containing the top-n ranking list of the top n-ranked users in the database. Returns null on a Redis connection error.
        /// </returns>
        public Dictionary<string, double> GetTopList(int topNumber)
        {
            try
            {
                return db.SortedSetRangeByRankWithScores("UserPool", 0, topNumber, Order.Descending).ToStringDictionary();
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during GetTopList: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return null;                   //Return null for a Redis connection error
            }
        }

        //Method to get number of logged in users
        /// <summary>
        /// The GetLoginPoolSize method returns the total number of logged in users in the database.
        /// </summary>
        /// <returns>
        /// The method returns the total number of logged in users in the database. Returns -1 on a Redis connection error.
        /// </returns>
        public long GetLoginPoolSize()
        {
            try
            {
                return db.SetLength("LoginPool");
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during GetLoginPoolSize: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return -1;                   //Return -1 for a Redis connection error
            }
        }

        //Method to get the number of dummy users (logged) in
        /// <summary>
        /// The GetDummyPoolSize method returns the total number of dummy users in the database.
        /// </summary>
        /// <returns>
        /// The method returns the total number of dummy users in the database. Returns -1 on a Redis connection error.
        /// </returns>
        public long GetDummyPoolSize()
        {
            try
            {
                return db.SetLength("DummyPool");
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during GetDummyPoolSize: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return -1;                   //Return -1 for a Redis connection error
            }
        }

        //Method to get all logged in user info
        /// <summary>
        /// The GetLoginList method returns a string[] containing the names of all logged in users in the database.
        /// </summary>
        /// <returns>
        /// The method returns a string[] containing the names of all logged in users in the database. Returns null on a Redis connection error.
        /// </returns>
        public string[] GetLoginList()
        {
            try
            {
                return db.SetMembers("LoginPool").ToStringArray();
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during GetLoginList: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return null;                   //Return null for a Redis connection error
            }
        }

        //Method to get all dummy user info
        /// <summary>
        /// The GetDummyList method returns a string[] containing the names of all dummy users in the database.
        /// </summary>
        /// <returns>
        /// The method returns a string[] containing the names of all dummy users in the database. Returns null on a Redis connection error.
        /// </returns>
        public string[] GetDummyList()
        {
            try
            {
                return db.SetMembers("DummyPool").ToStringArray();
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during GetDummyList: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return null;                   //Return null for a Redis connection error
            }
        }

        //Method to get the user rank list based on an intersection with another list
        //NOTE: This function yields message counts +1 greater than their actual value!!
        /// <summary>
        /// The GetSubTopList method returns a Dictionary containing the top-n ranking list of the top n-ranked users in the database intersected with another Set in the database. WARNING: The resultant scores in the dictionary are all 
        /// 1 greater than their true values. Please adjust accordingly.
        /// </summary>
        /// <remarks>
        /// WARNING: The Redis set intersection is performed between a ZSet and a Set to get a ZSet. Because the Set members do not have scores, Redis assumes all scores equal 1. In the score aggregation options provided 
        /// by Redis, the chosen scores can be min, max, or sum. Due to the score = 1 default, all of these options create rank sets with incorrect score values. Thus, the method was designed with the Aggregate.Sum 
        /// option and the caller is warned that the score values of the resultant set are 1 greater than their actual values.
        /// </remarks>
        /// <param name="topNumber">The top-n number of rank entries desired to be examined.</param>
        /// <param name="poolName">The name of the Redis set used for the intersection.</param>
        /// <returns>
        /// This method returns a Dictionary containing the top-n ranking list of the top n-ranked users in the database intersected with another Set in the database. Returns null on a Redis connection error.
        /// </returns>
        public Dictionary<string, double> GetSubTopList(int topNumber, string poolName)
        {
            try
            {
                db.SortedSetCombineAndStore(SetOperation.Intersect, poolName + "Ranked", "UserPool", poolName, Aggregate.Sum);              //Create a temporary ranked login pool
                db.KeyExpire(poolName + "Ranked", new TimeSpan(0, 1, 0));                                                                   //Let the pool expire after 1 minute
                return db.SortedSetRangeByRankWithScores(poolName + "Ranked", 0, topNumber, Order.Descending).ToStringDictionary();         //Return the dictionary
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during GetSubTopList: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return null;                                                                                                                //Return null for a Redis connection error
            }
        }

        /*****
            Methods for
            Rooms
        *****/

        //Create Room
        /// <summary>
        /// The RoomCreate method creates a new room in the Redis database. Return values: (-2) Redis Connection Error; (-1) Room already exists; (0) Owner is not a registered user; (1) Success.
        /// </summary>
        /// <param name="roomNumber">The room number.</param>
        /// <param name="roomTitle">The title or topic of the room.</param>
        /// <param name="owner">The username of the user who created the room.</param>
        /// <param name="serverID">A server ID to indicate which server the room's thread is run on.</param>
        /// <returns>
        /// The method returns an integer value to report one of 3 cases:
        /// <list type="definition">
        ///     <item>
        ///         <term>-2</term>
        ///         <description>-The connection with the Redis server failed.</description>
        ///     </item>
        ///     <item>
        ///         <term>-1</term>
        ///         <description>-The room number already exists.</description>
        ///     </item>
        ///     <item>
        ///         <term>0</term>
        ///         <description>-The owner is not a registered user.</description>
        ///     </item>
        ///     <item>
        ///         <term>1</term>
        ///         <description>-The room has been successfully created.</description>
        ///     </item>
        /// </list>
        /// </returns>
        public int RoomCreate (uint roomNumber, string roomTitle, string owner, uint serverID)
        {
            if (RoomExist(roomNumber) == false)
            {
                //Check if owner exists
                if (!DoesUsernameExist (owner))
                {
                    return 0;                                                       //Return 0 if the owner does not exist
                }
                try
                {
                    db.SortedSetAdd("RoomPool", "Room:" + roomNumber, 0);           //Add entry to the room pool with 0 people
                    db.HashSet("Room:" + roomNumber, "RoomTitle", roomTitle);       //Create hash with room's information
                    db.HashSet("Room:" + roomNumber, "RoomOwner", owner);           //Create hash with room's information
                    db.HashSet("Room:" + roomNumber, "ServerID", serverID);         //Create hash with room's information
                    db.SetAdd("Server:" + serverID, "Room:" + roomNumber);          //Add the room to the server's room pool
                    db.SortedSetIncrement("ServerPool", "Server:" + serverID, 1);   //Increase the room count of the server
                }
                catch (Exception e)
                {
                    Console.WriteLine("Redis server connection error during RoomCreate: " + e.Message);
                    Console.WriteLine("\t\tSource: " + e.Message);
                    return -2;                                                      //Return -2 for a Redis connection error
                }

                RoomAddUser(roomNumber, owner);
                return 1;                                                           //Return 1 for success
            }
            return -1;                                                              //Return -1 if room already exists
        }

        //Destroy Room
        /// <summary>
        /// The RoomDelete method deletes a room in the Redis database.
        /// </summary>
        /// <remarks>
        /// This function is made private so the only way a room can be deleted is when the final user is removed.
        /// </remarks>
        /// <param name="roomNumber">The room number.</param>
        /// <returns>
        /// The method returns true upon success and false if the room does not exist.
        /// </returns>
        private bool RoomDelete (uint roomNumber)
        {
            if (!RoomExist(roomNumber))
            {
                return false;                                                                                       //Return false if room does not exist
            }
            try
            {
                uint serverID = (uint)db.HashGet("Room:" + roomNumber, "ServerID");                                 //Get the room's server ID
                db.SetRemove("Server:" + serverID, "Room:" + roomNumber);                                           //Remove the room from the server ID's room pool
                db.SortedSetDecrement("ServerPool", "Server:" + serverID, 1);                                       //Decrease the room count of the server
                db.KeyDelete("Room:" + roomNumber + ":Contents");                                                   //Remove room::contents key
                db.KeyDelete("Room:" + roomNumber);                                                                 //Remove room key
                db.SortedSetRemove("RoomPool", "Room:" + roomNumber);                                               //Remove entry from room pool
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during RoomDelete: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return false;                                                                                       //Return false for a Redis connection error
            }
            return true;
        }

        //***Retrieve Room Data Functions***

        //Room existence
        /// <summary>
        /// The RoomExist method checks the existence of a room.
        /// </summary>
        /// <param name="roomNumber">The room number.</param>
        /// <returns>
        /// The method returns true if the room exists and false if the room does not exist.
        /// </returns>
        public bool RoomExist (uint roomNumber)
        {
            try
            {
                if (db.KeyExists("Room:" + roomNumber))         //Check if the room's key exists
                {
                    return true;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during RoomExist: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return false;                                   //Return false for a Redis connection error
            }
            return false;
        }

        //Check if a user is in a room
        /// <summary>
        /// The RoomContainsUser method checks if a user is in a particular room. Return values: (-3) Redis Connection Error; (-2) User does not exist; 
        /// (-1) Room does not exist; (0) User is not in the room; (1) User is in the room.
        /// </summary>
        /// <param name="roomNumber">The room number.</param>
        /// <param name="name">The username of the user who may be in the room.</param>
        /// <returns>
        /// The method returns an integer value to report one of 4 cases:
        /// <list type="definition">
        ///     <item>
        ///         <term>-3</term>
        ///         <description>-The connection with the Redis server failed.</description>
        ///     </item>
        ///     <item>
        ///         <term>-2</term>
        ///         <description>-The user does not exist.</description>
        ///     </item>
        ///     <item>
        ///         <term>-1</term>
        ///         <description>-The room does not exist.</description>
        ///     </item>
        ///     <item>
        ///         <term>0</term>
        ///         <description>-The user is not in the room.</description>
        ///     </item>
        ///     <item>
        ///         <term>1</term>
        ///         <description>-The user is in the room.</description>
        ///     </item>
        /// </list>
        /// </returns>
        public int RoomContainsUser (uint roomNumber, string name)
        {
            if (!RoomExist(roomNumber))
            {
                return -1;                                                      //Return -1 if room does not exist
            }
            if (!DoesUsernameExist(name))
            {
                return -2;                                                      //Return -2 if user does not exist
            }
            try
            {
                if (db.SetContains("Room:" + roomNumber + ":Contents", "User:" + name))
                {
                    return 1;                                                   //Return 1 if user is in the room
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during RoomContainsUser: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return -3;                                                      //Return -3 for a Redis connection error
            }
            return 0;                                                           //Return 0 if user is not in the room
        }

        //Get Room Title
        /// <summary>
        /// The RoomGetTitle method outputs the title of a room and returns a boolean value for success.
        /// </summary>
        /// <param name="roomNumber">The room number.</param>
        /// <param name="roomTitle">A container for the determined room title.</param>
        /// <returns>
        /// The method returns true if the room exists and false if the room does not exist.
        /// </returns>
        public bool RoomGetTitle (uint roomNumber, out string roomTitle)
        {
            roomTitle = null;                                                   //Set the default out-string value
            if (!RoomExist(roomNumber))
            {
                return false;                                                   //Return false if room does not exist
            }
            try
            {
                roomTitle = db.HashGet("Room:" + roomNumber, "RoomTitle");      //Get the title from the room's hash
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during RoomGetTitle: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return false;                                                   //Return false for a Redis connection error
            }
            return true;
        }

        //Get room user count
        /// <summary>
        /// The RoomGetUserCount method returns the number of users in a particular room, (-1) if the room does not exist, or (-2) on a Redis connection error.
        /// </summary>
        /// <param name="roomNumber">The room number.</param>
        /// <returns>
        /// The method returns the number of users in a particular room, (-1) if the room does not exist, or (-2) on a Redis connection error.
        /// </returns>
        public int RoomGetUserCount (uint roomNumber)
        {
            if (!RoomExist(roomNumber))
            {
                return -1;                                                              //Return -1 if room does not exist
            }
            try
            {
                return (int)db.SortedSetScore("RoomPool", "Room:" + roomNumber);        //Get the user count from the room's sorted set entry
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during RoomGetUserCount: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return -2;                                                              //Return -2 for a Redis connection error
            }
        }

        //Get room owner
        /// <summary>
        /// The RoomGetOwner method outputs the name of the owner of a particular room and returns a boolean value for success.
        /// </summary>
        /// <param name="roomNumber">The room number.</param>
        /// <param name="roomOwner">A container for the determined room owner name.</param>
        /// <returns>
        /// The method returns true if the room exists and false if the room does not exist.
        /// </returns>
        public bool RoomGetOwner(uint roomNumber, out string roomOwner)
        {
            roomOwner = null;                                                   //Set the default out-string value
            if (!RoomExist(roomNumber))
            {
                return false;                                                   //Return false if room does not exist
            }
            try
            {
                roomOwner = db.HashGet("Room:" + roomNumber, "RoomOwner");      //Get the owner's name from the room's hash
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during RoomGetOwner: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return false;                                                   //Return false for a Redis connection error
            }
            return true;
        }

        //Method to get a room's server ID
        /// <summary>
        /// The RoomGetServerID method outputs the room's server ID and returns a boolean value for success.
        /// </summary>
        /// <param name="roomNumber">The room number.</param>
        /// <param name="serverID">A container for the determined server ID.</param>
        /// <returns>
        /// The method returns true if the room exists and false if the room does not exist.
        /// </returns>
        public bool RoomGetServerID(uint roomNumber, out uint serverID)
        {
            serverID = 0;                                                       //Set the default out-uint value
            if (!RoomExist(roomNumber))
            {
                return false;                                                   //Return false if room does not exist
            }
            try
            {
                serverID = (uint)db.HashGet("Room:" + roomNumber, "ServerID");  //Get the owner's name from the room's hash
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during RoomGetServerID: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return false;                                                   //Return false for a Redis connection error
            }
            return true;
        }

        //Get room's size rank
        /// <summary>
        /// The RoomGetUserCount method returns the size ranking of a particular room, (-1) if the room does not exist, or (-2) on a Redis connection error.
        /// </summary>
        /// <param name="roomNumber">The room number.</param>
        /// <returns>
        /// The method returns the size ranking of a particular room, (-1) if the room does not exist, or (-2) on a Redis connection error.
        /// </returns>
        public int RoomGetSizeRank (uint roomNumber)
        {
            if (!RoomExist(roomNumber))
            {
                return -1;                                                                                  //Return -1 if room does not exist
            }
            try
            {
                return ((int)db.SortedSetRank("RoomPool", "Room:" + roomNumber, Order.Descending) + 1);     //Return room's rank (in descending order) [+1 because the index starts at 0]
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during RoomGetSizeRank: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return -2;                                                                                  //Return -2 for a Redis connection error
            }
        }

        //Change room name
        /// <summary>
        /// The RoomChangeTitle method attempts to change the title of a room. Upon success, it outputs the old room name. Return values: (-2) on a Redis connection error; 
        /// (-1) Room does not exist; (0) New room title = old room title; (1) Success.
        /// </summary>
        /// <param name="roomNumber">The room number.</param>
        /// <param name="newRoomTitle">The desired new title for the room.</param>
        /// <param name="oldRoomTitle">A container for the old room title.</param>
        /// <returns>
        /// The method returns an integer value to report one of 3 cases:
        /// <list type="definition">
        ///     <item>
        ///         <term>-2</term>
        ///         <description>-The connection with the Redis server failed.</description>
        ///     </item>
        ///     <item>
        ///         <term>-1</term>
        ///         <description>-The room does not exist.</description>
        ///     </item>
        ///     <item>
        ///         <term>0</term>
        ///         <description>-The new title is the same as the old title.</description>
        ///     </item>
        ///     <item>
        ///         <term>1</term>
        ///         <description>-The room title change was successful.</description>
        ///     </item>
        /// </list>
        /// </returns>
        public int RoomChangeTitle(uint roomNumber, string newRoomTitle, out string oldRoomTitle)
        {
            oldRoomTitle = null;                                                //Set the default out-string value
            if (!RoomExist(roomNumber))
            {
                return -1;                                                      //Return -1 if room does not exist
            }
            try
            {
                oldRoomTitle = db.HashGet("Room:" + roomNumber, "RoomTitle");   //Get the title from the room's hash
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during RoomChangeTitle: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return -2;                                                      //Return -2 for a Redis connection error
            }
            if (oldRoomTitle == newRoomTitle)
            {
                return 0;                                                       //Return 0 if the new room title is the same as before
            }
            db.HashSet("Room:" + roomNumber, "RoomTitle", newRoomTitle);        //Set the new title to the room's hash
            return 1;
        }

        //Set room owner
        /// <summary>
        /// The RoomSetOwner method attempts to change the owner of a room. Upon success, it outputs the old owner name. Return values: (-2) on a Redis connection error; 
        /// (-1) Room does not exist; (0) New owner = old owner; (1) Success.
        /// </summary>
        /// <param name="roomNumber">The room number.</param>
        /// <param name="newRoomOwner">The desired new owner for the room.</param>
        /// <param name="oldRoomOwner">A container for the old owner.</param>
        /// <returns>
        /// The method returns an integer value to report one of 3 cases:
        /// <list type="definition">
        ///     <item>
        ///         <term>-2</term>
        ///         <description>-The connection with the Redis server failed.</description>
        ///     </item>
        ///     <item>
        ///         <term>-1</term>
        ///         <description>-The room does not exist.</description>
        ///     </item>
        ///     <item>
        ///         <term>0</term>
        ///         <description>-The new owner is the same as the old owner.</description>
        ///     </item>
        ///     <item>
        ///         <term>1</term>
        ///         <description>-The room owner change was successful.</description>
        ///     </item>
        /// </list>
        /// </returns>
        public int RoomSetOwner(uint roomNumber, string newRoomOwner, out string oldRoomOwner)
        {
            oldRoomOwner = null;                                                    //Set the default out-string value
            if (!RoomExist(roomNumber))
            {
                return -1;                                                          //Return -1 if room does not exist
            }
            try
            {
                oldRoomOwner = db.HashGet("Room:" + roomNumber, "RoomOwner");       //Get the owner name from the room's hash
                if (oldRoomOwner == newRoomOwner)
                {
                    return 0;                                                       //Return 0 if the new owner name is the same as before
                }
                db.HashSet("Room:" + roomNumber, "RoomOwner", newRoomOwner);        //Set the new owner name to the room's hash
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during RoomSetOwner: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return -2;                                                          //Return -2 for a Redis connection error
            }
            return 1;
        }

        //Method to change a server ID
        /// <summary>
        /// The RoomSetServerID method attempts to change the server ID of a room. Upon success, it outputs the old server ID. Return values: (-2) on a Redis connection error; 
        /// (-1) Room does not exist; (0) New ID = old ID; (1) Success.
        /// </summary>
        /// <param name="roomNumber">The room number.</param>
        /// <param name="newServerID">The desired new server ID for the room.</param>
        /// <param name="oldServerID">A container for the old server ID.</param>
        /// <returns>
        /// The method returns an integer value to report one of 3 cases:
        /// <list type="definition">
        ///     <item>
        ///         <term>-2</term>
        ///         <description>-The connection with the Redis server failed.</description>
        ///     </item>
        ///     <item>
        ///         <term>-1</term>
        ///         <description>-The room does not exist.</description>
        ///     </item>
        ///     <item>
        ///         <term>0</term>
        ///         <description>-The new server ID is the same as the old server ID.</description>
        ///     </item>
        ///     <item>
        ///         <term>1</term>
        ///         <description>-The room server ID change was successful.</description>
        ///     </item>
        /// </list>
        /// </returns>
        public int RoomSetServerID(uint roomNumber, uint newServerID, out uint oldServerID)
        {
            oldServerID = 0;                                                        //Set the default out-string value
            if (!RoomExist(roomNumber))
            {
                return -1;                                                          //Return -1 if room does not exist
            }
            try
            {
                oldServerID = (uint)db.HashGet("Room:" + roomNumber, "ServerID");   //Get the owner name from the room's hash
                if (oldServerID == newServerID)
                {
                    return 0;                                                       //Return 0 if the new owner name is the same as before
                }
                db.SortedSetDecrement("ServerPool", "Server:" + oldServerID, 1);    //Decrease the room count of the old server ID
                db.SortedSetIncrement("ServerPool", "Server:" + newServerID, 1);    //Increase the room count of the new server ID
                db.SetRemove("Server:" + oldServerID, "Room:" + roomNumber);        //Remove the room from the old server ID's room pool
                db.SetAdd("Server:" + newServerID, "Room:" + roomNumber);           //Add the room to the new server ID's room pool
                db.HashSet("Room:" + roomNumber, "ServerID", newServerID);          //Set the new owner name to the room's hash
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during RoomSetServerID: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return -2;                                                          //Return -2 for a Redis connection error
            }
            return 1;
        }

        //Enter room
        /// <summary>
        /// The RoomAddUser method attempts to add a user to a room. Return values: (-3) on a Redis connection error; (-2) Room already contains user; 
        /// (-1) Room does not exist; (0) User does not exist; (1) Success.
        /// </summary>
        /// <param name="roomNumber">The room number.</param>
        /// <param name="name">The name of the user.</param>
        /// <returns>
        /// The method returns an integer value to report one of 4 cases:
        /// <list type="definition">
        ///     <item>
        ///         <term>-3</term>
        ///         <description>-The connection with the Redis server failed.</description>
        ///     </item>
        ///     <item>
        ///         <term>-2</term>
        ///         <description>-The room already contains the user.</description>
        ///     </item>
        ///     <item>
        ///         <term>-1</term>
        ///         <description>-The room does not exist.</description>
        ///     </item>
        ///     <item>
        ///         <term>0</term>
        ///         <description>-The user does not exist.</description>
        ///     </item>
        ///     <item>
        ///         <term>1</term>
        ///         <description>-The user was successfully added to the room.</description>
        ///     </item>
        /// </list>
        /// </returns>
        public int RoomAddUser (uint roomNumber, string name)
        {
            if (!RoomExist(roomNumber))
            {
                return -1;                                                      //Return -1 if room does not exist
            }
            if (!DoesUsernameExist(name))
            {
                return 0;                                                       //Return 0 if user does not exist
            }
            if (RoomContainsUser(roomNumber, name) == 1)
            {
                return -2;                                                      //Return -2 if the room already contains the user
            }
            try
            {
                db.SortedSetIncrement("RoomPool", "Room:" + roomNumber, 1);     //Add one to the room's user count
                db.SetAdd("Room:" + roomNumber + ":Contents", "User:" + name);  //Add the user to the room's user pool
                db.SetRemove("LobbyPool", "User:" + name);                      //Remove user from the lobby pool
                db.HashSet("User:" + name, "RoomNumber", roomNumber);           //Record that the user is in the room
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during CreateUser: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return -3;                                                      //Return -3 for a Redis connection error
            }
            return 1;                                                           //Return 1 on success
        }

        //Leave room
        /// <summary>
        /// The RoomRemoveUser method attempts to remove a user to a room. Return values: (-4) on a Redis connection error; (-3) User is not in the room; 
        /// (-2) Room deletion attempt failed [no more users in room]; 
        /// (-1) Room does not exist; (0) User does not exist; (1) User removed and owner still is in room; (2) User removed and the room was destroyed [no more users in room];
        /// (3) Owner was removed and an error occurred during owner change; (4) Owner removed and owner successfully changed.
        /// </summary>
        /// <param name="roomNumber">The room number.</param>
        /// <param name="name">The name of the user.</param>
        /// <returns>
        /// The method returns an integer value to report one of 9 cases:
        /// <list type="definition">
        ///     <item>
        ///         <term>-4</term>
        ///         <description>-The connection with the Redis server failed.</description>
        ///     </item>
        ///     <item>
        ///         <term>-3</term>
        ///         <description>-The user is not in the room.</description>
        ///     </item>
        ///     <item>
        ///         <term>-2</term>
        ///         <description>-The last user was removed from the room and subsequent room deletion failed.</description>
        ///     </item>
        ///     <item>
        ///         <term>-1</term>
        ///         <description>-The room does not exist.</description>
        ///     </item>
        ///     <item>
        ///         <term>0</term>
        ///         <description>-The user does not exist.</description>
        ///     </item>
        ///     <item>
        ///         <term>1</term>
        ///         <description>-The user was successfully removed from the room and the owner remains in the room.</description>
        ///     </item>
        ///     <item>
        ///         <term>2</term>
        ///         <description>-The user was successfully removed from the room and the room was successfully destroyed.</description>
        ///     </item>
        ///     <item>
        ///         <term>3</term>
        ///         <description>-The owner was removed from the room and an ownership transfer failed.</description>
        ///     </item>
        ///     <item>
        ///         <term>4</term>
        ///         <description>-The owner was removed from the room and a new owner was selected successfully.</description>
        ///     </item>
        /// </list>
        /// </returns>
        public int RoomRemoveUser(uint roomNumber, string name)
        {
            if (!RoomExist(roomNumber))
            {
                return -1;                                                                      //Return -1 if room does not exist
            }
            if (!DoesUsernameExist(name))
            {
                return 0;                                                                       //Return 0 if user does not exist
            }
            if (RoomContainsUser (roomNumber, name) != 1)
            {
                return -3;                                                                      //Return -3 if the user is not in the room          
            }
            try
            {
                db.SortedSetDecrement("RoomPool", "Room:" + roomNumber, 1);                     //Remove one from the room's user count
                db.SetRemove("Room:" + roomNumber + ":Contents", "User:" + name);               //Remove the user from the room's user pool
                db.SetAdd("LobbyPool", "User:" + name);                                         //Add user from the lobby pool
                db.HashSet("User:" + name, "RoomNumber", 0);                                    //Record that the user is back in the lobby
                if (db.SortedSetScore("RoomPool", "Room:" + roomNumber) < 1)
                {
                    if (RoomDelete(roomNumber))                                                  //If the room user count dropped to 0, delete the room
                    {
                        return 2;                                                               //Return 2 to indicate that the room was destroyed
                    }
                    return -2;                                                                  //Return -2 if the room should have been deleted but wasn't possible for an unknown reason                                                 
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during RoomRemoveUser: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return -4;                                                                      //Return -4 for a Redis connection error
            }

            try
            {
                //If there are still people in the room, we need to transfer the owner to a different user (if the person removed was the owner!)
                //First, check if the removed user was the owner
                if (string.Compare(db.HashGet("Room:" + roomNumber, "RoomOwner"), name) == 0)
                {
                    string newOwner = db.SetRandomMember("Room:" + roomNumber + ":Contents");   //Get a random user from the room's pool
                    if (newOwner == null)
                    {
                        return 3;                                                               //Return 3 if for some unknown reason no member name was obtained
                    }
                    db.HashSet("Room:" + roomNumber, "RoomOwner", newOwner);                    //Set the new owner name to the room's hash
                    return 4;                                                                   //Return 4 to indicate the owner was changed
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during RoomRemoveUser: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return -4;                                                                      //Return -4 for a Redis connection error
            }
            return 1;                                                                           //Return 1 on success and the owner didn't need changing
        }

        //Method to purge a room of users and subsequently delete
        /// <summary>
        /// The RoomPurge method purges a room of users and ensures deletion of a room. Return values: (-2) on a Redis connection error; (-1) Room does not exist; (0) Room empty and can't delete; 
        /// (1) Purge successful; (2) Room empty and deletion successful;
        /// </summary>
        /// <param name="roomNumber">The room number.</param>
        /// <returns>
        /// The method returns an integer value to report one of 4 cases:
        /// <list type="definition">
        ///     <item>
        ///         <term>-2</term>
        ///         <description>-The connection with the Redis server failed.</description>
        ///     </item>
        ///     <item>
        ///         <term>-1</term>
        ///         <description>-The room does not exist.</description>
        ///     </item>
        ///     <item>
        ///         <term>0</term>
        ///         <description>-The room was empty and deletion resulted in error.</description>
        ///     </item>
        ///     <item>
        ///         <term>1</term>
        ///         <description>-The room was successfully purged.</description>
        ///     </item>
        ///     <item>
        ///         <term>2</term>
        ///         <description>-The room was empty and subsequently deleted.</description>
        ///     </item>
        /// </list>
        /// </returns>
        public int RoomPurge (uint roomNumber)
        {
            if (!RoomExist(roomNumber))
            {
                return -1;                                                                  //Return -1 if room does not exist
            }
            RedisValue[] roomUsers = null;
            try
            {
                roomUsers = db.SetMembers("Room:" + roomNumber + ":Contents");              //Get the users listed in the room
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during RoomPurge: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return -2;                                                                  //Return -2 for a Redis connection error
            }
            if (roomUsers == null)
            {
                if (RoomDelete(roomNumber))
                {
                    return 2;                                                               //Return 2 if the room was found empty and the room was successfully deleted
                }
                return 0;                                                                   //Return 0 if the room was empty and the room couldn't be deleted
            }
            foreach (RedisValue user in roomUsers)
            {
                RoomRemoveUser(roomNumber, user.ToString().Substring(5));                   //For each user, attempt to remove the user from the room (ignore error cases)
            }
            if (RoomExist(roomNumber))
            {
                RoomDelete(roomNumber);                                                     //If the room didn't delete from failures, delete it forcefully
            }
            return 1;                                                                       //Return 1 if purge successful
        }

        //***Room Statistics***

        //Get all users in room
        /// <summary>
        /// The RoomUserList method returns a string[] containing the names of all users in a particular room. Returns null on Redis connection error.
        /// </summary>
        /// <param name="roomNumber">The room number.</param>
        /// <returns>
        /// The method returns a string[] containing the names of all users in a particular room. Returns null on Redis connection error.
        /// </returns>
        public string[] RoomUserList(uint roomNumber)
        {
            if (!RoomExist(roomNumber))
            {
                return null;                                                    //Return null if room does not exist
            }
            try
            {
                return db.SetMembers("Room:" + roomNumber + ":Contents").ToStringArray();
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during RoomUserList: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return null;                                                    //Return null for a Redis connection error
            }
        }

        //Get members in the lobby
        /// <summary>
        /// The GetLobbyUserList method returns a string[] containing the names of all users in the lobby. Returns null on Redis connection error.
        /// </summary>
        /// <returns>
        /// The method returns a string[] containing the names of all users in the lobby. Returns null on Redis connection error.
        /// </returns>
        public string[] GetLobbyUserList ()
        {
            try
            {
                return db.SetMembers("LobbyPool").ToStringArray();
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during GetLobbyUserList: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return null;                                                     //Return null for a Redis connection error
            }
        }

        //Get room list
        /// <summary>
        /// The RoomList method returns a string[] containing the names of all rooms active in the Redis database. Returns null on Redis connection error.
        /// </summary>
        /// <returns>
        /// The method returns a string[] containing the names of all rooms active in the Redis database. Returns null on Redis connection error.
        /// </returns>
        public string[] RoomList()
        {
            try
            {
                return db.SortedSetRangeByRank("RoomPool", 0, -1, Order.Descending).ToStringArray();
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during RoomList: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return null;                                                     //Return null for a Redis connection error
            }
        }

        //Get room list
        /// <summary>
        /// The RoomListByServerID method returns a string[] containing the names of all rooms active in a particular server. Returns null on Redis connection error.
        /// </summary>
        /// <param name="serverID">The ID of the server in which the room subset is desired.</param>
        /// <returns>
        /// The method returns a string[] containing the names of all rooms active in a particular server. Returns null on Redis connection error.
        /// </returns>
        public string[] RoomListByServerID(uint serverID)
        {
            try
            {
                return db.SetMembers("Server:" + serverID).ToStringArray();
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during RoomListByServerID: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return null;                                                    //Return null for a Redis connection error
            }
        }

        //Get room list rankings (with score)
        /// <summary>
        /// The RoomSizeRankList method returns a Dictionary containing the top-n ranking list of the top n-ranked rooms in the database in terms of their population. Returns null on Redis connection error.
        /// </summary>
        /// <param name="topNumber">The top-n number of rank entries desired to be examined.</param>
        /// <returns>
        /// This method returns a Dictionary containing the top-n ranking list of the top n-ranked rooms in the database in terms of their population. Returns null on Redis connection error.
        /// </returns>
        public Dictionary<string, double> RoomSizeRankList(int topNumber)
        {
            try
            {
                return db.SortedSetRangeByRankWithScores("RoomPool", 0, topNumber, Order.Descending).ToStringDictionary();
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during RoomSizeRankList: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return null;                                                    //Return null for a Redis connection error
            }
        }

        //***Server Information Storage***

        //Method to get a server's room count
        /// <summary>
        /// The ServerGetRoomCount method returns the number of rooms in a particular server or (-1) if the server ID does not exist. Returns (-2) on a Redis connection failure.
        /// </summary>
        /// <param name="serverID">The server ID.</param>
        /// <returns>
        /// The method returns the number of rooms in a particular server or (-1) if the server ID does not exist. Returns (-2) on a Redis connection failure.
        /// </returns>
        public int ServerGetRoomCount(uint serverID)
        {
            double? roomCount = 0;
            try
            {
                roomCount = db.SortedSetScore("ServerPool", "Server:" + serverID);              //Retrieve the room count from the server pool
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during CreateUser: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return -2;                                                                      //Return -2 for a Redis connection error
            }
            if (roomCount == null)
            {
                return -1;                                                                      //Return -1 if server ID does not exist
            }
            return (int)roomCount;                                                              //Return the room count from the server ID's sorted set entry
        }

        //Method to get the server room count rankings
        /// <summary>
        /// The RoomSizeRankList method returns a Dictionary containing the top-n ranking list of the top n-ranked servers in the database in terms of their room count. Returns null on Redis connection error.
        /// </summary>
        /// <param name="topNumber">The top-n number of rank entries desired to be examined.</param>
        /// <returns>
        /// This method returns a Dictionary containing the top-n ranking list of the top n-ranked servers in the database in terms of their room count. Returns null on Redis connection error.
        /// </returns>
        public Dictionary<string, double> ServerRoomCountRanking(int topNumber)
        {
            try
            {
                return db.SortedSetRangeByRankWithScores("ServerPool", 0, topNumber, Order.Descending).ToStringDictionary();
            }
            catch (Exception e)
            {
                Console.WriteLine("Redis server connection error during CreateUser: " + e.Message);
                Console.WriteLine("\t\tSource: " + e.Message);
                return null;                                                    //Return null for a Redis connection error
            }
        }
    }
}
