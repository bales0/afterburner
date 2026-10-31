/* Offline stress tests of the AVR sparse fusemap codec.
 * Run from repository root:
 *   g++ -std=c++17 -fsanitize=address,undefined -o /tmp/afterburner_sparse_test tests/sparse_regression.cpp
 *   /tmp/afterburner_sparse_test
 * Does not require Arduino, and does not test real GAL chips.
 */
#include <cstdint>
#include <cassert>
#include <cstring>
#include <iostream>
#define F(s) s
struct MockSerial {
    void print(const char*) {};
    void println(const char*) {};
    void print(unsigned, int) {};
    void println(unsigned, int) {};
};
MockSerial Serial;
static constexpr int DEC=10;
#define USE_SPARSE_FUSEMAP
static uint8_t fusemap[1332];
#include "../aftb_sparse.h"

static bool store(uint16_t bit) {
    uint16_t pos=sparseSetFuseBit(bit);
    if (pos==0xFF01) return true;
    if (pos>=sizeof(fusemap)) return false;
    fusemap[pos] |= 1u<<(bit&7);
    return true;
}
static bool read(uint16_t bit) {
    uint16_t pos=sparseGetFuseBit(bit);
    if (pos>=0xFF00) return (pos&1)!=0;
    assert(pos<sizeof(fusemap));
    return (fusemap[pos] & (1u<<(bit&7)))!=0;
}
int main() {
    sparseInit(1);
    std::memset(fusemap,0,sizeof(fusemap));
    // Sparse enough: every 50th bit. Verify previous and repeated writes.
    for (uint16_t i=0;i<14499;i+=50) assert(store(i));
    for (uint16_t i=0;i<14499;i++) assert(read(i)==(i%50==0));
    for (uint16_t i=0;i<14499;i+=50) assert(store(i));
    for (uint16_t i=0;i<14499;i++) assert(read(i)==(i%50==0));
    // Compaction converts a full intermediate group to type 3 (implicit ones).
    // A repeated write must not corrupt a later group's stored data.
    sparseInit(1);
    std::memset(fusemap,0,sizeof(fusemap));
    for (uint16_t i=0;i<96;i++) assert(store(i));
    sparseCompactFuseMap();
    for (uint16_t i=0;i<96;i++) assert(read(i));
    assert(store(40));
    for (uint16_t i=0;i<96;i++) assert(read(i));
    // Full-width dense map cannot fit, but it must be rejected safely.
    sparseInit(1);
    std::memset(fusemap,0,sizeof(fusemap));
    bool refused=false;
    for (uint16_t i=0;i<14499;i+=32) {
        if (!store(i)) { refused=true; break; }
    }
    assert(refused);
    std::cout << "PASS: sparse codec, duplicate bits, and capacity guard\n";
}
