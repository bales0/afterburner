/* Offline stress tests of the ACTUAL AVR sparse fusemap implementation.
 * Run from repository root:
 *   g++ -std=c++17 -O1 -g -fsanitize=address,undefined -fno-omit-frame-pointer \
 *       -o /tmp/afterburner_sparse_test tests/sparse_regression.cpp
 *   /tmp/afterburner_sparse_test
 * No real programmer is needed. The bounds guard detects writes beyond the fuse buffer.
 */
#include <cstdint>
#include <cassert>
#include <cstring>
#include <iostream>
#define F(s) s
struct MockSerial {
    void print(const char*) {}
    void println(const char*) {}
    void print(unsigned, int) {}
    void println(unsigned, int) {}
};
MockSerial Serial;
static constexpr int DEC=10;
#define USE_SPARSE_FUSEMAP
struct GuardedFusemap {
    uint32_t before[4];
    alignas(uint32_t) uint8_t data[1332];
    uint32_t after[4];
};
static GuardedFusemap guarded;
#define fusemap guarded.data
#include "../aftb_sparse.h"

static void checkGuards() {
    for (unsigned i=0; i<4; ++i) {
        assert(guarded.before[i] == 0x12345678u);
        assert(guarded.after[i] == 0x87654321u);
    }
}
static void reset() {
    std::memset(fusemap, 0, sizeof(fusemap));
    for (unsigned i=0; i<4; ++i) {
        guarded.before[i] = 0x12345678u;
        guarded.after[i] = 0x87654321u;
    }
    sparseInit(1);
    checkGuards();
}
static bool store(uint16_t bit) {
    assert(bit < 14499); // ATF750C fusemap
    uint16_t pos=sparseSetFuseBit(bit);
    if (pos == 0xFF01) return true;
    if (pos >= sizeof(fusemap)) { checkGuards(); return false; }
    fusemap[pos] |= 1u<<(bit&7);
    checkGuards();
    return true;
}
static bool read(uint16_t bit) {
    assert(bit < 14499);
    uint16_t pos=sparseGetFuseBit(bit);
    if (pos>=0xFF00) return (pos&1)!=0;
    assert(pos<sizeof(fusemap));
    return (fusemap[pos] & (1u<<(bit&7)))!=0;
}
int main() {
    // No bits (erased) must occupy zero bytes.
    reset();
    for (uint16_t i=0;i<14499;i++) assert(!read(i));
    assert((sparseFusemapStat & 0x7FF) == 0);

    // Sparse data must survive unordered/repeated writes and random reads.
    reset();
    for (uint16_t i=0;i<14499;i+=50) assert(store(i));
    for (uint16_t i=0;i<14499;i++) assert(read(i)==(i%50==0));
    for (uint16_t i=0;i<14499;i+=50) assert(store(i));
    for (uint16_t i=0;i<14499;i++) assert(read(i)==(i%50==0));

    // The compactor must preserve implicit all-one groups and repeated writes.
    reset();
    for (uint16_t i=0;i<96;i++) assert(store(i));
    sparseCompactFuseMap();
    for (uint16_t i=0;i<96;i++) assert(read(i));
    assert(store(40));
    for (uint16_t i=0;i<96;i++) assert(read(i));
    checkGuards();

    // Exact limit: 333 distinct mixed 32-bit groups use 1332 bytes.
    // Group 334 must fail WITHOUT modifying the buffer or the index.
    reset();
    for (uint16_t group=0;group<333;group++) assert(store(group*32));
    assert((sparseFusemapStat & 0x7FF) == sizeof(fusemap));
    assert(!store(333*32));
    assert((sparseFusemapStat & 0x7FF) == sizeof(fusemap));
    assert(!read(333*32));
    for (uint16_t group=0;group<333;group++) {
        assert(read(group*32));
        assert(!read(group*32+1));
    }
    checkGuards();

    // Worst-case fragmented ATF750C: 454 mixed groups cannot fit in 1332 B.
    // Pseudo-random bit offsets exercise arbitrary positions within each group.
    reset();
    uint16_t stored=0;
    for (uint16_t group=0;group<454;group++) {
        const uint16_t bit = group*32 + ((group*17u+3u)%32u);
        if (bit>=14499) break;
        if (!store(bit)) break;
        stored++;
    }
    assert(stored==333);
    for (uint16_t group=0;group<stored;group++) {
        const uint16_t bit = group*32 + ((group*17u+3u)%32u);
        assert(read(bit));
    }
    checkGuards();

    // All-one map is highly compressible; verify real compaction stays correct.
    reset();
    for (uint16_t bit=0;bit<14499;bit++) assert(store(bit));
    for (uint16_t bit=0;bit<14499;bit++) assert(read(bit));
    assert((sparseFusemapStat & 0x7FF) <= sizeof(fusemap));
    checkGuards();

    std::cout << "PASS: ATF750C sparse capacity, 333-group boundary, fragmented maps, compaction, canaries\n";
}
