/*!
 * \brief   The file contains Hash LINQ implementation
 * \author  \verbatim
            Created by: Alexander Egorov
            \endverbatim
 * \date    \verbatim
            Creation date: 2011-11-14
            \endverbatim
 * Copyright: (c) Alexander Egorov 2009-2013
 */

#include "targetver.h"
#include <locale.h>
#include "rdr.h"
#include "argtable2.h"
#include "apr.h"
#include "apr_pools.h"
#include "apr_strings.h"
#include "apr_file_io.h"
#include "pcre.h"
#include "sqlite3.h"

#ifdef WIN32
#include "..\srclib\DebugHelplers.h"
#endif

#define ERROR_BUFFER_SIZE 2 * BINARY_THOUSAND
#define LINE_FEED '\n'

#define PATH_ELT_SEPARATOR '\\'
#define NUMBER_PARAM_FMT_STRING "%lu"
#define BIG_NUMBER_PARAM_FMT_STRING "%llu"
#define START_MSG "^\\s*\\[?\\d{4}-\\d{2}-\\d{2}\\s+\\d{2}:\\d{2}:\\d{2}([,.]\\d{3,})?.*"
#define TRACE_LEVEL "TRACE|\\[[Tt]race\\]"
#define DEBUG_LEVEL "DEBUG|\\[[Dd]ebug\\]"
#define INFO_LEVEL "INFO|\\[[Ii]nfo\\]"
#define WARN_LEVEL "WARN|\\[[Ww]arn\\]"
#define ERROR_LEVEL "ERROR|\\[[Ee]rror\\]"
#define FATAL_LEVEL "FATAL|\\[[Ff]atal\\]"
#define CACHE_SIZE "PRAGMA cache_size = %lu ;"
#define MMAP_SIZE "PRAGMA mmap_size= %lu ;"

#define MAX_LINE_SIZE 32 * BINARY_THOUSAND - 1
#define MAX_STRING_LEN 8192
#define PAGE_SIZE 1024

apr_pool_t* pcrePool = NULL;

void PrintError(apr_status_t status)
{
    char errbuf[ERROR_BUFFER_SIZE];
    apr_strerror(status, errbuf, ERROR_BUFFER_SIZE);
    CrtPrintf("%s", errbuf); //-V111
    NewLine();
}

void* PcreAlloc(size_t size)
{
    return apr_palloc(pcrePool, size);
}

static int Callback(void *NotUsed, int argc, char **argv, char **azColName){
   int i;
   for(i=0; i<argc; i++){
      CrtPrintf("%s = %s\n", azColName[i], argv[i] ? argv[i] : "NULL");
   }
   CrtPrintf("\n");
   return 0;
}

size_t GetTotalSystemMemory()
{
    MEMORYSTATUSEX status;
    status.dwLength = sizeof(status);
    GlobalMemoryStatusEx(&status);
    return status.ullTotalPhys;
}

int main(int argc, const char* const argv[])
{
    apr_pool_t* pool = NULL;
    apr_status_t status = APR_SUCCESS;
    int nerrors;

    pcre* re = NULL;
    pcre* reT = NULL;
    pcre* reD = NULL;
    pcre* reI = NULL;
    pcre* reW = NULL;
    pcre* reE = NULL;
    pcre* reF = NULL;
    
    
    const char* error = NULL;
    int erroffset = 0;
    int rc = 0;
    int flags  = PCRE_NOTEMPTY;

    apr_file_t* fileHandle = NULL;
    char* line = NULL;
    
    long long messages = 0;
    long long msgT = 0;
    long long msgD = 0;
    long long msgI = 0;
    long long msgW = 0;
    long long msgE = 0;
    long long msgF = 0;
    
    Time time = { 0 };

    apr_finfo_t info = { 0 };
    size_t systemMemory = 0;
    size_t avaiablePages = 0;

    sqlite3 *db = NULL;
    char *zErrMsg = 0;
    const char* createTable;
    const char* syncOff = "PRAGMA synchronous = OFF;";
    const char* journal = "PRAGMA journal_mode = MEMORY;";
    const char* tempStore = "PRAGMA temp_store = 2;";
    const char* encoding = "PRAGMA encoding = 'UTF-8';";
    char* cacheSize = NULL;
    char* mmapSize = NULL;

    struct arg_file *file          = arg_file0("f", "file", NULL, "full path to log file");
    struct arg_lit  *help          = arg_lit0("h", "help", "print this help and exit");
    struct arg_end  *end           = arg_end(10);

    void* argtable[] = { file, help, end };

#ifdef WIN32
#ifndef _DEBUG  // only Release configuration dump generating
    SetUnhandledExceptionFilter(TopLevelFilter);
#endif
#endif

    setlocale(LC_ALL, ".ACP");
    setlocale(LC_NUMERIC, "C");

    pcre_malloc = PcreAlloc;

    status = apr_app_initialize(&argc, &argv, NULL);
    if (status != APR_SUCCESS) {
        CrtPrintf("Couldn't initialize APR");
        NewLine();
        PrintError(status);
        return EXIT_FAILURE;
    }
    atexit(apr_terminate);
    apr_pool_create(&pool, NULL);

    if (arg_nullcheck(argtable) != 0) {
        PrintSyntax(argtable);
        goto cleanup;
    }

    /* Parse the command line as defined by argtable[] */
    nerrors = arg_parse(argc, argv, argtable);

    if (help->count > 0) {
        PrintSyntax(argtable);
        goto cleanup;
    }
    if (nerrors > 0 || argc < 2) {
        arg_print_errors(stdout, end, PROGRAM_NAME);
        PrintSyntax(argtable);
        goto cleanup;
    }

    pcrePool = pool; // needed for pcre_alloc (PcreAlloc) function

    re = pcre_compile(START_MSG, PCRE_UTF8, &error, &erroffset, 0);
    
    reT = pcre_compile(TRACE_LEVEL, PCRE_UTF8, &error, &erroffset, 0);
    reD = pcre_compile(DEBUG_LEVEL, PCRE_UTF8, &error, &erroffset, 0);
    reI = pcre_compile(INFO_LEVEL, PCRE_UTF8, &error, &erroffset, 0);
    reW = pcre_compile(WARN_LEVEL, PCRE_UTF8, &error, &erroffset, 0);
    reE = pcre_compile(ERROR_LEVEL, PCRE_UTF8, &error, &erroffset, 0);
    reF = pcre_compile(FATAL_LEVEL, PCRE_UTF8, &error, &erroffset, 0);
    
    if (!re || !reT || !reD || !reI || !reW || !reE || !reF) {
        CrtPrintf("%s", error); //-V111
        goto cleanup;
    }

    status = apr_file_open(&fileHandle, file->filename[0], APR_FOPEN_READ | APR_FOPEN_BUFFERED, APR_FPROT_WREAD, pool);
    if (status != APR_SUCCESS) {
        PrintError(status);
        goto cleanup;
    }

    status = apr_file_info_get(&info, APR_FINFO_MIN | APR_FINFO_NAME, fileHandle);

    if (status != APR_SUCCESS) {
        PrintError(status);
        goto cleanup;
    }

    rc = sqlite3_open_v2("log.db", &db, SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE, NULL);

    if(rc){
      CrtPrintf("Can't open database: %s\n", sqlite3_errmsg(db));
      goto cleanup;
    }

    createTable = "CREATE TABLE IF NOT EXISTS Log ( \
                                 Ix INTEGER PRIMARY KEY AUTOINCREMENT, \
                                 Header TEXT  NOT NULL, \
                                 Body  TEXT, \
                                 Level INTEGER NOT NULL \
                        ); \
        ";

    rc = sqlite3_exec(db, createTable, Callback, 0, &zErrMsg);
    if( rc != SQLITE_OK ){
        CrtPrintf("SQL error: %s\n", zErrMsg);
        sqlite3_free(zErrMsg);
    }
    rc = sqlite3_exec(db, syncOff, Callback, 0, &zErrMsg);
    if( rc != SQLITE_OK ){
        CrtPrintf("SQL error: %s\n", zErrMsg);
        sqlite3_free(zErrMsg);
    }
    rc = sqlite3_exec(db, journal, Callback, 0, &zErrMsg);
    if( rc != SQLITE_OK ){
        CrtPrintf("SQL error: %s\n", zErrMsg);
        sqlite3_free(zErrMsg);
    }
    rc = sqlite3_exec(db, tempStore, Callback, 0, &zErrMsg);
    if( rc != SQLITE_OK ){
        CrtPrintf("SQL error: %s\n", zErrMsg);
        sqlite3_free(zErrMsg);
    }
    rc = sqlite3_exec(db, encoding, Callback, 0, &zErrMsg);
    if( rc != SQLITE_OK ){
        CrtPrintf("SQL error: %s\n", zErrMsg);
        sqlite3_free(zErrMsg);
    }
    mmapSize = (char*)apr_pcalloc(pool, strlen(MMAP_SIZE) + 30);
    apr_snprintf(mmapSize, strlen(MMAP_SIZE) + 30, MMAP_SIZE, info.size);

    rc = sqlite3_exec(db, mmapSize, Callback, 0, &zErrMsg);
    if( rc != SQLITE_OK ){
        CrtPrintf("SQL error: %s\n", zErrMsg);
        sqlite3_free(zErrMsg);
    }

    systemMemory = GetTotalSystemMemory();
    avaiablePages = (systemMemory / PAGE_SIZE) / 5;
    cacheSize = (char*)apr_pcalloc(pool, strlen(CACHE_SIZE) + 30);
    apr_snprintf(cacheSize, strlen(CACHE_SIZE) + 30, CACHE_SIZE, MIN(avaiablePages, (info.size / PAGE_SIZE + 1)));

    rc = sqlite3_exec(db, cacheSize, Callback, 0, &zErrMsg);
    if( rc != SQLITE_OK ){
        CrtPrintf("SQL error: %s\n", zErrMsg);
        sqlite3_free(zErrMsg);
    }

    line = (char*)apr_pcalloc(pool, MAX_STRING_LEN);
    StartTimer();
    while (status != APR_EOF) {
        int len = 0;
        status = apr_file_gets(line, MAX_STRING_LEN, fileHandle);
        len = (int)strlen(line);
        rc = pcre_exec(re, 0, line, len, 0, flags, NULL, 0);
        if (rc >= 0) {
            ++messages;
            rc = pcre_exec(reT, 0, line, len, 0, flags, NULL, 0);
            if (rc >= 0) {
                ++msgT;
                continue;
            }
            rc = pcre_exec(reD, 0, line, len, 0, flags, NULL, 0);
            if (rc >= 0) {
                ++msgD;
                continue;
            }
            rc = pcre_exec(reI, 0, line, len, 0, flags, NULL, 0);
            if (rc >= 0) {
                ++msgI;
                continue;
            }
            rc = pcre_exec(reW, 0, line, len, 0, flags, NULL, 0);
            if (rc >= 0) {
                ++msgW;
                continue;
            }
            rc = pcre_exec(reE, 0, line, len, 0, flags, NULL, 0);
            if (rc >= 0) {
                ++msgE;
                continue;
            }
            rc = pcre_exec(reF, 0, line, len, 0, flags, NULL, 0);
            if (rc >= 0) {
                ++msgF;
                continue;
            }
        }
    }
    StopTimer();
    time = ReadElapsedTime();

    CrtPrintf(NEW_LINE "Messages: %llu Time " FULL_TIME_FMT, messages, time.hours, time.minutes, time.seconds);
    CrtPrintf(NEW_LINE "T:%llu D:%llu I:%llu W:%llu E:%llu F:%llu" NEW_LINE, msgT, msgD, msgI, msgW, msgE, msgF);
    sqlite3_close(db);
cleanup:
    /* deallocate each non-null entry in argtable[] */
    arg_freetable(argtable, sizeof(argtable) / sizeof(argtable[0]));
    apr_pool_destroy(pool);
    return EXIT_SUCCESS;
}

void PrintSyntax(void* argtable) {
    PrintCopyright();
    arg_print_syntax(stdout, argtable, NEW_LINE NEW_LINE);
    arg_print_glossary_gnu(stdout,argtable);
}

void PrintCopyright(void)
{
    CrtPrintf(COPYRIGHT_FMT, APP_NAME);
}
