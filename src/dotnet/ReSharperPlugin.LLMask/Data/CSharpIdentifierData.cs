using System;
using System.Collections.Generic;

namespace ReSharperPlugin.LLMask.Data;

/// <summary>
/// Static data for the C# language: keywords, contextual keywords, preprocessor
/// identifiers, and well-known BCL types that the obfuscator preserves by default.
/// </summary>
public static class CSharpIdentifierData
{
    /// <summary>
    /// Comma-separated list of identifiers that are preserved verbatim during
    /// obfuscation. Used as the default value for the <c>BaseWhitelist</c> setting.
    /// </summary>
    public const string DefaultBaseWhitelist =
        // C# keywords
        "abstract,as,base,bool,break,byte,case,catch,char," +
        "checked,class,const,continue,decimal,default,delegate,do," +
        "double,else,enum,event,explicit,extern,false,finally," +
        "fixed,float,for,foreach,goto,if,implicit,in,int," +
        "interface,internal,is,lock,long,namespace,new,null," +
        "object,operator,out,override,params,private,protected," +
        "public,readonly,ref,return,sbyte,sealed,short,sizeof," +
        "stackalloc,static,string,struct,switch,this,throw,true," +
        "try,typeof,uint,ulong,unchecked,unsafe,ushort,using," +
        "virtual,void,volatile,while," +
        // Contextual keywords
        "add,alias,ascending,async,await,by,descending,dynamic," +
        "equals,from,get,global,group,into,join,let,nameof," +
        "notnull,on,orderby,partial,remove,select,set,unmanaged," +
        "value,var,when,where,with,yield," +
        // Preprocessor identifiers
        "define,elif,endif,endregion,error,line,nullable," +
        "pragma,region,undef,warning," +
        // Well-known BCL / framework types
        "Action,Activator,AppDomain," +
        "ArgumentException,ArgumentNullException,ArgumentOutOfRangeException," +
        "Array,ArrayList,Attribute," +
        "Boolean,Byte," +
        "CancellationToken,CancellationTokenSource,Char," +
        "Console,Convert," +
        "DateTime,DateTimeOffset,Decimal,Dictionary," +
        "Double," +
        "Enum,Environment,EventArgs,EventHandler,Exception," +
        "Func," +
        "Guid," +
        "HashSet," +
        "ICollection,IComparable,IDisposable," +
        "IEnumerable,IEnumerator,IEquatable," +
        "IList,IReadOnlyCollection,IReadOnlyDictionary,IReadOnlyList," +
        "Int16,Int32,Int64,IntPtr," +
        "InvalidOperationException," +
        "KeyValuePair," +
        "Lazy,LinkedList,List," +
        "Math,MemoryStream,Monitor,Mutex," +
        "NotImplementedException,NotSupportedException," +
        "Nullable," +
        "Object,ObjectDisposedException," +
        "OperationCanceledException,OutOfMemoryException," +
        "Parallel,Path,Queue," +
        "Random,Regex," +
        "SByte,Semaphore,SemaphoreSlim,Single," +
        "SortedDictionary,SortedList,Stack,StackOverflowException," +
        "Stream,StreamReader,StreamWriter,String,StringBuilder," +
        "Task,Thread,ThreadPool,TimeSpan,Timer,Tuple," +
        "Type," +
        "UInt16,UInt32,UInt64,UIntPtr,Uri," +
        "ValueTask,ValueTuple,Version," +
        "WeakReference," +
        // Common attribute names used without the 'Attribute' suffix
        "Flags,NonSerialized,Obsolete,Serializable,ThreadStatic";
    
    /// <summary>
    /// Comma-separated list of well-known namespace root segments.
    /// Used as the default value for the <c>WellKnownNamespaceRoots</c> setting.
    /// </summary>
    public const string DefaultWellKnownNamespaceRoots =
        "System,Microsoft,Windows," +
        "Serilog,NLog,log4net," +
        "Xunit,NUnit,MSTest," +
        "FluentAssertions,Shouldly," +
        "Moq,NSubstitute,FakeItEasy," +
        "AutoFixture,Bogus," +
        "WireMock,Testcontainers," +
        "Verify," +
        "Swashbuckle,Grpc,RestSharp,Refit,Flurl," +
        "Newtonsoft,MessagePack,ProtoBuf,YamlDotNet," +
        "Dapper,LinqToDB,Marten," +
        "Npgsql,MongoDB,Elasticsearch,Elastic,Cassandra,Couchbase,LiteDB,RavenDB,MySql," +
        "StackExchange," +
        "RabbitMQ,MassTransit,Confluent,MediatR,Wolverine,NATS,NetMQ," +
        "Autofac,Castle,Ninject,SimpleInjector,Scrutor,Lamar,DryIoc," +
        "AutoMapper,Mapster,Riok," +
        "FluentValidation," +
        "Polly," +
        "Hangfire,Quartz,Coravel," +
        "Azure,Amazon,Google," +
        "OpenTelemetry,Sentry,Prometheus,App," +
        "OfficeOpenXml,ClosedXML,DocumentFormat,NPOI," +
        "CsvHelper,HtmlAgilityPack,AngleSharp," +
        "SixLabors,ImageMagick,SkiaSharp,OxyPlot," +
        "NodaTime," +
        "LanguageExt,OneOf,CSharpFunctionalExtensions," +
        "Humanizer,Spectre," +
        "MailKit,MimeKit,SendGrid," +
        "Twilio,Stripe," +
        "Akka,Proto,ReactiveUI," +
        "Avalonia,CommunityToolkit,Prism";

    // Root namespace segments whose using directives are preserved verbatim
    // because they don't reveal proprietary information.
    // Assembly-resolution (Pass 1c) also uses this set to decide whether a
    // resolved identifier belongs to a well-known library and should be kept.
    internal static readonly HashSet<string> WellKnownNamespaceRoots =
        new(DefaultWellKnownNamespaceRoots.Split(','), StringComparer.Ordinal);
}
