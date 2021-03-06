#include <map>
#include <string>

#pragma once

struct format_preprocess_block;
typedef UINT32 FpbUidType;

// using a 64bit stack entry covers all integral types: double, int, int32, int64, pointer etc...
typedef __int64 stack_t;

// ANSI_STRING and UNICODE_STRING, for %Z processing, are not defined in our environment.
// Or rather it seems some who include us do have them defined and some don't.  Sigh, as a
// quick fix define some identical types with unique names.
typedef struct _UNICODE_STRING_NT {
    USHORT Length;
    USHORT MaximumLength;
    __field_bcount_part(MaximumLength, Length) PWCH   Buffer;
} UNICODE_STRING_NT;

typedef struct _STRING_NT {
     USHORT Length;
    USHORT MaximumLength;
    __field_bcount_part_opt(MaximumLength, Length) PCHAR Buffer;
} STRING_NT;

typedef STRING_NT *PSTRING_NT;
typedef STRING_NT ANSI_STRING_NT;
typedef PSTRING_NT PANSI_STRING_NT;

// We also need a varient that does not contain any pointers and is amenable to being
// serialized, thus _counted_string (which handles both ANSI and UNICODE strings).
// When packed after LogEntry::m_args[], there cannot be any pointers.
#pragma warning(push)
#pragma warning(disable:4200) // nonstandard extension used : zero-sized array in struct/union
typedef struct _counted_string {
    unsigned short usLengthInBytes;
    union
    {
        char cBuffer[0];
        wchar_t wcBuffer[0];
    };
} counted_string;
#pragma warning(pop)

__inline bool
ValidUnicodeString (
    __in UNICODE_STRING_NT * s
    )
{
    //  Verify common requirements of ANSI and UNICODE counted strings.
    if ((s == NULL) ||
        (s->Buffer == NULL) ||
        (s->Length == 0) ||
        (s->Length > s->MaximumLength))
    {
        return false;
    }

    //  UNICODE_STRINGs must have an even number of bytes.
    if ((s->Length | s->MaximumLength) & 1)
    {
        return false;
    }

    return true;
}

__inline bool
ValidAnsiString (
    __in ANSI_STRING_NT * s
    )
{
    //  Verify common requirements of ANSI and UNICODE counted strings.
    if ((s == NULL) ||
        (s->Buffer == NULL) ||
        (s->Length == 0) ||
        (s->Length > s->MaximumLength))
    {
        return false;
    }

    return true;
}

//
// How to get around type-checking/type conversion constraints...
//
typedef union
{
    char     c;
    wchar_t  wc;
    short    s;
    int      n;
    unsigned int u;
    long     l;
    double   d;
    Int64    i64;
    UInt64   u64;
    stack_t  se;
    void    *pv;
    char    *pstr;
    wchar_t *pwstr;
    ANSI_STRING_NT *pAnsiStr;
    UNICODE_STRING_NT *pUnicodeStr;
    counted_string *pCountedStr;
} param;

// V1: 2010/09/10
// V2: 2014/03/31
// V3: Current version
#define LOG_ENTRY_VERSION   3

typedef INT32 LogParameterOffsetType;

//
// Used when serializing logs to generate back references.
//
typedef std::map<std::string, LogParameterOffsetType> LogParameterMap;

//
// Used when deserializing logs to parse back references.
//
typedef std::map<LogParameterOffsetType, std::string> LogParameterReverseMap;

union LogEntryMarker
{
    UInt16 m_asInt16;

    struct
    {
        UInt16 m_pad : 8;
        UInt16 m_haveActivityId : 1;
        UInt16 m_haveEntryPointId : 1;
        UInt16 m_haveTitle : 1;
        UInt16 m_titleInPreprocessBlock : 1;
        UInt16 m_useLastActivityId : 1;
        UInt16 m_useLastEntryPointId : 1;
        UInt16 m_useLastThreadId : 1;
        UInt16 m_useLastTimestamp : 1;
    } s1;

    LogEntryMarker(UInt16 val = 0)
    {
        m_asInt16 = val;
    }
};

C_ASSERT(sizeof(LogEntryMarker) == sizeof(UInt16));

#define BINARY_LOG_PROCESS_HEADER_MARKER 0xBB
#define BINARY_LOG_BYTE_ORDER_CHECK      0x0102

// Structure that is serialized in the binary log everytime a new
// process is started. Provides versioning and PID info.
struct BinaryLogProcessHeader
{
    UINT8 m_marker;
    UINT16 m_byteOrderCheck;
    UINT16 m_formatBlockVersion;
    UINT16 m_logEntryVersion;
    UINT16 m_paramDescVersion;
    UINT16 m_paramDescSerializedLen;
    DWORD m_pid;

    BinaryLogProcessHeader()
    {
        m_marker = 0;
        m_byteOrderCheck = 0;
        m_formatBlockVersion = 0;
        m_logEntryVersion = 0;
        m_paramDescVersion = 0;
        m_paramDescSerializedLen = 0;
        m_pid = 0;
    }

    void Init();

    bool Verify();
};

#pragma warning( disable: 4200 )

#define LOG_ENTRY_MARKER_BYTE 0xEE
class LogEntry
{
public:

    LogEntryMarker    m_logEntryMarker;
    UInt16            m_serializedLen;   // Length of serialized representation.

    FpbUidType        m_preprocessBlockUid;  // Identifier of the format block that knows how to parse this entry.
    unsigned long long m_backReferenceMask;  // Bitmask identifying parameters which have been saved as back-references.

    //
    // Any fields that are to be serialized to disk must be added before
    // this point.
    //

    format_preprocess_block * m_pPreprocessBlock;

    PCSTR           m_title;        // title string for this log entry
    GUID            m_ActivityId;   // activity Id of the entry.
    GUID            m_EntryPointId; // entry point Id of the entry.
    FILETIME        m_timestamp;
    DWORD           m_ThreadId;     // thread Id of log entry author.

    DWORD           m_argsSize;     // size in bytes of the argument block starting at m_args

    // Variable argument list parameters go here in the order specified
    // in the preprocess block.
    param m_args[0];

public:

    LogEntry()
    {
    }

public:

    int Serialize(
        __in_bcount(bufferLen) PCHAR pBufferIn,
        __in int bufferLen,
        __in const GUID *pLastActivityId,
        __in const GUID *pLastEntryId,
        __in FILETIME lastTimestamp,
        __in DWORD lastThreadId);

    static LogEntry *UnserializeEntryBase(
        __in UINT16 logEntryVersion,
        __in const char *pSourceBuffer,
        __in ULONG sourceBufferSize,
        __in const GUID *pLastActivityId,
        __in const GUID *pLastEntryId,
        __in FILETIME lastTimestamp,
        __in DWORD lastThreadId,
        __out PULONG pBytesRead,
        __in_bcount(targetBufferSize) char *pTargetBuffer,
        __in int targetBufferSize);

    DWORD UnserializeParameters(
        __in_bcount(bufferSize) const char *pBuffer,
        __in ULONG bufferSize,
        __in ULONG baseFileOffset,
        __in ULONG entryLen,
        __out PULONG pBytesRead,
        __in UINT16 logEntryVersion,
        __in LogParameterReverseMap& paramMap);

    DWORD UnserializeParametersV1(
        __in_bcount(bufferSize) const char *pReadBuffer,
        __in ULONG bufferSize,
        __in ULONG baseFileOffset,
        __in ULONG entryBufferUsed,
        __in ULONG entryLen,
        __out PULONG pBytesRead,
        __in LogParameterReverseMap& paramMap);

    void FixArgsOffsets();
    /*
public:

    static int GetBytesNeeded(
        __in format_preprocess_block *pPreprocessBlock,
        __inout int *varArgSizes,
        __in UInt16 varArgSizeArrayCount,
        __in va_list argptr);

private:

    static int GetVarSizeArgsSize(
        __in format_preprocess_block *pPreprocessBlock,
        __inout int *varArgSizes,
        __in UInt16 varArgSizeArrayCount,
        __in va_list argptr);
        */
private:
    // Deny copying and assignment.
    LogEntry(const LogEntry&);
    LogEntry& operator=(const LogEntry&);
};

#define LOG_ENTRY_HEADER_SIZE (FIELD_OFFSET(LogEntry, m_pPreprocessBlock))


