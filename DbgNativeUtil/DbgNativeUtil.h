// The following ifdef block is the standard way of creating macros which make exporting 
// from a DLL simpler. All files within this DLL are compiled with the DBGNATIVEUTIL_EXPORTS
// symbol defined on the command line. This symbol should not be defined on any project
// that uses this DLL. This way any other project whose source files include this file see 
// DBGNATIVEUTIL_API functions as being imported from a DLL, whereas this DLL sees symbols
// defined with this macro as being exported.
#ifdef DBGNATIVEUTIL_EXPORTS
#define DBGNATIVEUTIL_API __declspec(dllexport)
#else
#define DBGNATIVEUTIL_API __declspec(dllimport)
#endif

/*
extern DBGNATIVEUTIL_API int nDbgNativeUtil;

DBGNATIVEUTIL_API int fnDbgNativeUtil(void);
*/
