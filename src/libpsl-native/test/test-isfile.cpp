//! @file test-isfile.cpp
//! @author Andrew Schwartzmeyer <andschwa@microsoft.com>
//! @brief Tests Isfile

#include <gtest/gtest.h>
#include <errno.h>
#include <unistd.h>
#include "isfile.h"

TEST(IsFileTest, RootIsFile)
{
    EXPECT_TRUE(IsFile("/"));
}

TEST(IsFileTest, BinLsIsFile)
{
    EXPECT_TRUE(IsFile("/bin/ls"));
}

TEST(IsFileTest, CannotGetOwnerOfFakeFile)
{
    EXPECT_FALSE(IsFile("SomeMadeUpFileNameThatDoesNotExist"));
    EXPECT_EQ(errno, ERROR_FILE_NOT_FOUND);
}

TEST(IsFileTest, ReturnsFalseForNullInput)
{
    EXPECT_FALSE(IsFile(NULL));
    EXPECT_EQ(errno, ERROR_INVALID_PARAMETER);
}
